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
using System.Linq;
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
            // Deliberately not carried over: wasConditionTrue/firedAtUtc/the live poller state. A
            // clone starts fresh - its first sample is baseline-only (see ShouldFire), matching a
            // freshly-added trigger's own behavior rather than assuming the clone's history (or its
            // re-arm clock) is still relevant.
            // TriggerRunner is freshly constructed by this() above, so it starts empty already.
            foreach (var item in copyMe.TriggerRunner.Items) {
                TriggerRunner.Add((ISequenceItem)item.Clone());
            }
        }

        public override object Clone() {
            return new NightFrontSeeingTrigger(this);
        }

        /// <summary>
        /// Finds every NightFrontSeeingTrigger anywhere in the whole sequence tree containing
        /// <paramref name="from"/> (walks up to the ultimate root, same starting point as
        /// NightFrontContainer.FindAnywhere, then searches every descendant depth-first) and
        /// returns whichever has the most recent successful, non-null LastSampleTimeUtc/
        /// LastFwhmArcsec pair - or null if none exists or none has ever sampled successfully. Used
        /// by NightFrontReplanInstruction to feed a live seeing reading into the replan the same
        /// way it already reads live cloud cover from NINA's own weather mediator, in-process, no
        /// sidecar file of its own.
        ///
        /// Unlike NightFrontContainer.FindAnywhere (which only needs to find one container and can
        /// stop at the first match), this must consider every trigger site in the tree: a
        /// NightFrontSeeingTrigger attaches to a container via that container's Triggers
        /// collection, not its Items collection, so the search descends into both - and, for
        /// completeness on the pathological case of a Seeing Trigger's own action container
        /// nesting another Seeing Trigger, into each found trigger's own TriggerRunner as well.
        /// </summary>
        public static NightFrontSeeingTrigger FindMostRecentlySampled(ISequenceItem from) {
            ISequenceContainer root = from?.Parent;
            if (root == null) {
                return null;
            }
            while (root.Parent != null) {
                root = root.Parent;
            }

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var found = new List<NightFrontSeeingTrigger>();
            CollectFromContainer(root, visited, found);

            return found
                .Where(t => t.LastFwhmArcsec.HasValue && t.LastSampleTimeUtc.HasValue)
                .OrderByDescending(t => t.LastSampleTimeUtc)
                .FirstOrDefault();
        }

        private static void CollectFromContainer(ISequenceContainer container, HashSet<object> visited, List<NightFrontSeeingTrigger> found) {
            if (container == null || !visited.Add(container)) {
                return;
            }

            foreach (var item in container.Items) {
                if (item is ISequenceContainer nestedContainer) {
                    CollectFromContainer(nestedContainer, visited, found);
                }
            }

            if (container is ITriggerable triggerable) {
                foreach (var trigger in triggerable.Triggers) {
                    CollectFromTrigger(trigger, visited, found);
                }
            }
        }

        private static void CollectFromTrigger(ISequenceTrigger trigger, HashSet<object> visited, List<NightFrontSeeingTrigger> found) {
            if (trigger == null || !visited.Add(trigger)) {
                return;
            }

            if (trigger is NightFrontSeeingTrigger seeingTrigger) {
                found.Add(seeingTrigger);
                if (seeingTrigger.TriggerRunner != null) {
                    CollectFromContainer(seeingTrigger.TriggerRunner, visited, found);
                }
            }
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

        /// <summary>How long a live reading stays trusted to beat the forecast during a replan -
        /// mirrors NightFrontApp's own LIVE_OVERRIDE_WINDOW_SEC (forecast/LiveWeatherOverride.kt).
        /// Also the re-arm horizon below: keep both in sync if either changes.</summary>
        private static readonly TimeSpan LiveDataHorizon = TimeSpan.FromHours(2);

        private volatile SeeingSample latestSample;
        private bool? wasConditionTrue;
        private DateTime? firedAtUtc;

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
            // internal, not private: lets NightFrontSeeingTriggerTests seed a trigger's last-sample
            // state directly to test FindMostRecentlySampled without needing a live poller/host.
            internal set {
                lastFwhmArcsec = value;
                RaisePropertyChangedOnUIThread(nameof(LastFwhmArcsec));
            }
        }

        private DateTime? lastSampleTimeUtc;

        public DateTime? LastSampleTimeUtc {
            get => lastSampleTimeUtc;
            internal set {
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

            // latestSample always updates (ShouldTrigger does its own IsStale re-check against it),
            // but LastFwhmArcsec/LastSampleTimeUtc - the pair FindMostRecentlySampled reads to feed
            // a live reading into a replan - only update together, and only on a trusted (successful
            // AND fresh) sample, so neither the sequencer UI readout nor a replan ever sees a number
            // that's merely "whatever the last poll attempt returned, stale or not."
            latestSample = sample;

            if (!sample.Success) {
                StatusMessage = $"Sample failed: {sample.Error}";
                return;
            }

            if (SeeingSampler.IsStale(sample, DateTime.Now, TimeSpan.FromMinutes(PollingIntervalMinutes * 2))) {
                StatusMessage = $"Stale data (on-image time {sample.ReportedAtLocal})";
                return;
            }

            LastFwhmArcsec = sample.FwhmArcsec;
            LastSampleTimeUtc = DateTime.UtcNow;
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

            var timeSinceFired = firedAtUtc.HasValue ? (TimeSpan?)(DateTime.UtcNow - firedAtUtc.Value) : null;
            var fire = ShouldFire(conditionTrueNow, wasConditionTrue, timeSinceFired, LiveDataHorizon);
            wasConditionTrue = conditionTrueNow;
            if (fire) {
                firedAtUtc = DateTime.UtcNow;
            } else if (!conditionTrueNow) {
                firedAtUtc = null;
            }
            return fire;
        }

        /// <summary>
        /// Pure edge-trigger decision, extracted for unit testing without a live sample/poller.
        /// wasConditionTrue == null means "no sample evaluated yet" - the first-ever real sample
        /// only establishes the baseline and never fires by itself, even if it already reads on the
        /// "true" side of the threshold (a plain `bool wasConditionTrue = false` default would
        /// wrongly treat "already true at trigger startup" as a fresh crossing). Otherwise fires on
        /// the explicit false-to-true transition; a true-to-true streak or any transition to false
        /// does not fire - UNLESS the condition has been continuously true since longer ago than
        /// rearmAfter (timeSinceFired &gt;= rearmAfter), in which case a still-true reading is
        /// treated as a fresh crossing and fires again. This matters because a fired reading is only
        /// trusted to override the forecast for rearmAfter (the same live-data horizon
        /// NightFrontApp's own replan blend uses) - persistently good/bad conditions should keep
        /// producing a fresh replan every time that window lapses, not just once for the whole
        /// spell.
        /// </summary>
        internal static bool ShouldFire(bool conditionTrueNow, bool? wasConditionTrue, TimeSpan? timeSinceFired, TimeSpan rearmAfter) {
            if (wasConditionTrue == true && timeSinceFired.HasValue && timeSinceFired.Value >= rearmAfter) {
                wasConditionTrue = false;
            }
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
