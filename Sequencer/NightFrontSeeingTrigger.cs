using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NINA.Core.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Periodically samples a remote seeing-monitor data source (see SeeingSampler) and, only on
    /// the transition from not-satisfied to satisfied against a user threshold/comparator, runs a
    /// user-populated TriggerRunner action container - a generic condition-to-action bridge, not
    /// tied to NightFront's own plan-execution machinery. A NightFrontReplanInstruction dropped
    /// into the action container gets seeing-triggered replanning "for free," with no special-case
    /// code here.
    ///
    /// TriggerRunner wiring and the sequencer-editor drop-area XAML both follow the pattern in
    /// palmito9/Nina.SequencerPlus's DIYMeridianFlipTrigger (a real, published plugin confirmed to
    /// target the same NINA.Plugin 3.2.0.9001 this dev environment runs) rather than upstream
    /// NINA's own CustomTrigger, whose ItemUtility.CreateTriggerRunnerContext helper and supporting
    /// XAML styles are not present in any NINA version this plugin can target or that's actually
    /// deployed (verified by string search against the installed assemblies).
    ///
    /// Sampling runs on a decoupled background poller (started in Initialize, stopped in
    /// Teardown), not synchronously inside ShouldTrigger - ShouldTrigger is a synchronous method
    /// called by NINA's own engine at an unconfirmed thread context, and a blocking
    /// GetAwaiter().GetResult() on an await-based HTTP+OCR chain is the textbook
    /// SynchronizationContext deadlock shape if that thread ever turns out to be the UI dispatcher.
    /// ShouldTrigger instead does a fast, synchronous read of the poller's cached latest sample.
    /// </summary>
    [ExportMetadata("Name", "Seeing Trigger")]
    [ExportMetadata("Description", "Periodically samples a seeing-monitor data source and, when the reading crosses your threshold, runs the instructions you drop below.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontSeeingTrigger : SequenceTrigger, IValidatable {

        [ImportingConstructor]
        public NightFrontSeeingTrigger() {
            TriggerRunner = new SequentialContainer { Name = "Seeing Trigger Actions" };
        }

        private NightFrontSeeingTrigger(NightFrontSeeingTrigger copyMe) : this() {
            CopyMetaData(copyMe);
            ThresholdArcsec = copyMe.ThresholdArcsec;
            Comparator = copyMe.Comparator;
            PollingIntervalMinutes = copyMe.PollingIntervalMinutes;
            DataSourceUrlOverride = copyMe.DataSourceUrlOverride;
            // Deliberately not carried over: wasConditionTrue/the live poller state. A clone starts
            // fresh - its first sample is baseline-only (see ShouldFire), matching a freshly-added
            // trigger's own behavior rather than assuming the clone's history is still relevant.
            // TriggerRunner is freshly constructed by this() above, so it starts empty already.
            foreach (var item in copyMe.TriggerRunner.Items) {
                TriggerRunner.Add((ISequenceItem)item.Clone());
            }
        }

        public override object Clone() {
            return new NightFrontSeeingTrigger(this);
        }

        [JsonProperty]
        public double ThresholdArcsec { get; set; } = 2.5;

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public SeeingComparator Comparator { get; set; } = SeeingComparator.LessThanOrEqual;

        /// <summary>How often the background poller actually samples the data source. Not how
        /// often NINA calls ShouldTrigger - that happens at whatever cadence the engine's own
        /// trigger-check points fire; this only rate-limits the real network+OCR work.</summary>
        [JsonProperty]
        public double PollingIntervalMinutes { get; set; } = 5;

        /// <summary>Optional per-trigger override of the plugin-wide Settings.Default
        /// .SeeingDataSourceUrl - blank auto-detects (uses the global option), same "blank
        /// auto-detects" precedent as NightFrontWhileCalibrationRemainsCondition.BaseName.</summary>
        [JsonProperty]
        public string DataSourceUrlOverride { get; set; } = "";

        public IList<string> Issues { get; set; } = new List<string>();

        private volatile SeeingSample latestSample;
        private bool? wasConditionTrue;

        private CancellationTokenSource pollerCts;
        private Task pollerTask;

        private string statusMessage = "Not yet sampled";

        public string StatusMessage {
            get => statusMessage;
            private set {
                statusMessage = value;
                RaisePropertyChangedOnUIThread(nameof(StatusMessage));
            }
        }

        private double? lastFwhmArcsec;

        public double? LastFwhmArcsec {
            get => lastFwhmArcsec;
            private set {
                lastFwhmArcsec = value;
                RaisePropertyChangedOnUIThread(nameof(LastFwhmArcsec));
            }
        }

        private DateTime? lastSampleTimeUtc;

        public DateTime? LastSampleTimeUtc {
            get => lastSampleTimeUtc;
            private set {
                lastSampleTimeUtc = value;
                RaisePropertyChangedOnUIThread(nameof(LastSampleTimeUtc));
            }
        }

        /// <summary>Same pattern as NightFrontUpdateInstruction/NightFrontReplanInstruction:
        /// StatusMessage/LastFwhmArcsec/LastSampleTimeUtc are bound in the sequencer UI, which WPF
        /// requires only be touched from the Dispatcher thread, but the poller updates them from a
        /// background Task.</summary>
        private void RaisePropertyChangedOnUIThread(string propertyName) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.Invoke(() => RaisePropertyChanged(propertyName));
            } else {
                RaisePropertyChanged(propertyName);
            }
        }

        public override void AfterParentChanged() {
            TriggerRunner?.AttachNewParent(Parent);
        }

        public override void Initialize() {
            StartPoller();
        }

        public override void Teardown() {
            StopPoller();
        }

        public override void SequenceBlockTeardown() {
            StopPoller();
        }

        private void StartPoller() {
            if (pollerTask != null) {
                return;
            }
            pollerCts = new CancellationTokenSource();
            var token = pollerCts.Token;
            pollerTask = Task.Run(() => PollLoopAsync(token), token);
        }

        private void StopPoller() {
            try {
                pollerCts?.Cancel();
            } catch (ObjectDisposedException) {
                // Already disposed by a previous Teardown - harmless, both hooks can fire.
            }
            pollerTask = null;
        }

        private async Task PollLoopAsync(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                await SampleOnceAsync().ConfigureAwait(false);
                try {
                    var delay = TimeSpan.FromMinutes(Math.Max(0.1, PollingIntervalMinutes));
                    await Task.Delay(delay, token).ConfigureAwait(false);
                } catch (TaskCanceledException) {
                    return;
                }
            }
        }

        private async Task SampleOnceAsync() {
            var url = ResolveDataSourceUrl();
            if (string.IsNullOrWhiteSpace(url)) {
                StatusMessage = "No seeing data source URL configured (set it on the NightFront Options tab or this trigger's override).";
                return;
            }

            SeeingSample sample;
            try {
                sample = await SeeingSampler.SampleAsync(new Uri(url), ResolveTessdataDir()).ConfigureAwait(false);
            } catch (Exception ex) {
                sample = new SeeingSample(false, null, null, null, "", ex.Message);
            }

            latestSample = sample;
            LastSampleTimeUtc = DateTime.UtcNow;

            if (!sample.Success) {
                StatusMessage = $"Sample failed: {sample.Error}";
                LastFwhmArcsec = null;
                return;
            }

            if (SeeingSampler.IsStale(sample, DateTime.Now, TimeSpan.FromMinutes(PollingIntervalMinutes * 2))) {
                StatusMessage = $"Stale data (on-image time {sample.ReportedAtLocal})";
                LastFwhmArcsec = sample.FwhmArcsec;
                return;
            }

            LastFwhmArcsec = sample.FwhmArcsec;
            StatusMessage = $"FWHM {sample.FwhmArcsec:0.00}\" as of {sample.ReportedAtLocal?.ToString("t") ?? "unknown time"}";
        }

        private string ResolveDataSourceUrl() {
            return string.IsNullOrWhiteSpace(DataSourceUrlOverride) ? Settings.Default.SeeingDataSourceUrl : DataSourceUrlOverride;
        }

        private static string ResolveTessdataDir() {
            var asmDir = Path.GetDirectoryName(typeof(NightFrontSeeingTrigger).Assembly.Location);
            return Path.Combine(asmDir ?? "", "tessdata");
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            var sample = latestSample;
            if (sample == null || !sample.Success) {
                return false;
            }
            if (SeeingSampler.IsStale(sample, DateTime.Now, TimeSpan.FromMinutes(PollingIntervalMinutes * 2))) {
                return false;
            }

            var fwhm = sample.FwhmArcsec.Value;
            var conditionTrueNow = Comparator == SeeingComparator.LessThanOrEqual
                ? fwhm <= ThresholdArcsec
                : fwhm >= ThresholdArcsec;

            var fire = ShouldFire(conditionTrueNow, wasConditionTrue);
            wasConditionTrue = conditionTrueNow;
            return fire;
        }

        /// <summary>
        /// Pure edge-trigger decision, extracted for unit testing without a live sample/poller.
        /// wasConditionTrue == null means "no sample evaluated yet" - the first-ever real sample
        /// only establishes the baseline and never fires by itself, even if it already reads on the
        /// "true" side of the threshold (a plain `bool wasConditionTrue = false` default would
        /// wrongly treat "already true at trigger startup" as a fresh crossing). Fires only on the
        /// explicit false-to-true transition; a true-to-true streak or any transition to false does
        /// not fire.
        /// </summary>
        internal static bool ShouldFire(bool conditionTrueNow, bool? wasConditionTrue) {
            return wasConditionTrue == false && conditionTrueNow;
        }

        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            return false;
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            await TriggerRunner.Run(progress, token);
        }

        public bool Validate() {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(ResolveDataSourceUrl())) {
                issues.Add("No seeing data source URL configured. Set one on the NightFront Options tab, or override it on this trigger.");
            }

            TriggerRunner?.Validate();
            if (TriggerRunner != null) {
                foreach (var issue in TriggerRunner.Issues) {
                    issues.Add(issue);
                }
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Trigger: {nameof(NightFrontSeeingTrigger)}, Threshold: {Comparator} {ThresholdArcsec}\"";
        }
    }
}
