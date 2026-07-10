using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// The primary mechanism from todos/nina-safety-delay-plan.md's Phase 3: placed by the user
    /// once inside (or immediately after) the "Once Safe" branch of their own template - the point
    /// their sequence *already* falls back to on recovery from a safety-monitor interruption, right
    /// before control returns to the top of "Loop while safe" (see the plan doc's Finding 3 for why
    /// that native restart-from-top behavior is exactly what makes this insertion point safe: by
    /// the time this instruction runs, nothing inside "Loop while safe" - including the real
    /// NightFrontContainer - is executing). On execution it:
    /// 1. Finds NightFrontContainer anywhere in the sequence (see NightFrontContainer.FindAnywhere -
    ///    unlike NightFrontUpdateInstruction, this instruction has no fixed positional relationship
    ///    to the container, since "Once Safe" and "Loop while safe" are sibling branches, not a
    ///    preceding-sibling arrangement) and reads its already-live progress
    ///    (NightFrontContainer.BuildProgressSnapshot, Phase 1) - completed subframes per target,
    ///    and which targets haven't started yet tonight.
    /// 2. Reads current live weather/safety state via NINA's own ISafetyMonitorMediator/
    ///    IWeatherDataMediator, the same GetInfo()-based pattern NightFrontMetadataRecorder already
    ///    uses for IRotatorMediator.
    /// 3. Writes the progress snapshot and (if available) a live-weather-override JSON file into
    ///    the configured NightFront data folder, then spawns the NightFront CLI (Settings.Default.
    ///    NightFrontCliPath) as `replan --effort=&lt;ReplanEffortLevel&gt; &lt;session-config.json&gt;
    ///    &lt;progress-snapshot&gt; &lt;weather-override|none&gt; &lt;output-plan&gt;
    ///    [selection-preference.json]` - see BuildReplanArguments.
    /// 4. Waits (bounded by ReplanTimeoutSeconds) for the process to exit and write the output plan.
    /// 5. Calls the existing NightFrontContainer.PopulateItems to fully repopulate the container
    ///    with the fresh plan - safe here specifically because nothing in "Loop while safe" is
    ///    executing at this point (Finding 3/6). If the CLI exits successfully but writes no output
    ///    file - NightFront's own replan mode does exactly this when every target is already fully
    ///    imaged, see Main.kt's runReplan - the container is instead emptied (not left with its old,
    ///    already-completed contents), so "Loop while safe" restarting from the top doesn't re-shoot
    ///    a night that's actually finished.
    ///
    /// Requires NightFrontApp's exporter to have also written `session-config.json`/`selection.json`
    /// sidecars (see ScheduleScreen.kt/NightFrontMetadataPaths.cs) - the plugin otherwise has no way
    /// to know where NightFront's own input config for tonight's plan lives, since it only ever
    /// reads the already-transformed NINA sequence JSON.
    ///
    /// KNOWN GAP, not solved here: there is no packaged, installer-shipped NightFront CLI executable
    /// yet (the MSI/AppImage/deb installers only build the Compose Desktop GUI's launcher). Until
    /// one exists, NightFrontCliPath must point at a user-provided wrapper (e.g. a .bat running
    /// `java -jar NightFront.jar %*`) - see Nightfront.cs's own doc comment on that setting.
    /// </summary>
    [ExportMetadata("Name", "Replan After Safety Recovery")]
    [ExportMetadata("Description", "Place inside (or immediately after) your sequence's own 'Once Safe' recovery branch. Reads tonight's live progress and current weather, re-solves the remainder of the night via the NightFront CLI, and repopulates the NightFront Container with the fresh plan before 'Loop while safe' restarts from the top.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontReplanInstruction : SequenceItem, IValidatable {

        /// <summary>How long to wait for the NightFront CLI subprocess before giving up. Generous
        /// relative to even the slowest measured effort preset (Thorough ~76.5s on a dev desktop,
        /// see CLAUDE.md's Genetic Algorithm section) to allow real headroom on the underpowered
        /// imaging-PC hardware this whole effort-level default change was motivated by, while still
        /// bounding how long a stuck/hung subprocess can block sequence recovery.</summary>
        public const int ReplanTimeoutSeconds = 300;

        private readonly IProfileService profileService;
        private readonly NightFrontJsonImporter importer;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IWeatherDataMediator weatherDataMediator;

        [ImportingConstructor]
        public NightFrontReplanInstruction(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IImageSaveMediator imageSaveMediator,
            IImageHistoryVM imageHistoryVM,
            IFilterWheelMediator filterWheelMediator,
            IGuiderMediator guiderMediator,
            IFocuserMediator focuserMediator,
            IAutoFocusVMFactory autoFocusVMFactory,
            ITelescopeMediator telescopeMediator,
            IRotatorMediator rotatorMediator,
            IDomeMediator domeMediator,
            IDomeFollower domeFollower,
            IPlateSolverFactory plateSolverFactory,
            IWindowServiceFactory windowServiceFactory,
            INighttimeCalculator nighttimeCalculator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IPlanetariumFactory planetariumFactory,
            IList<IDateTimeProvider> dateTimeProviders,
            ISafetyMonitorMediator safetyMonitorMediator,
            IWeatherDataMediator weatherDataMediator)
            : this(
                profileService,
                new NightFrontJsonImporter(
                    profileService,
                    cameraMediator,
                    imagingMediator,
                    imageSaveMediator,
                    imageHistoryVM,
                    filterWheelMediator,
                    guiderMediator,
                    focuserMediator,
                    autoFocusVMFactory,
                    telescopeMediator,
                    rotatorMediator,
                    domeMediator,
                    domeFollower,
                    plateSolverFactory,
                    windowServiceFactory,
                    nighttimeCalculator,
                    framingAssistantVM,
                    applicationMediator,
                    planetariumFactory,
                    dateTimeProviders),
                safetyMonitorMediator,
                weatherDataMediator) {
        }

        private NightFrontReplanInstruction(IProfileService profileService, NightFrontJsonImporter importer, ISafetyMonitorMediator safetyMonitorMediator, IWeatherDataMediator weatherDataMediator) {
            this.profileService = profileService;
            this.importer = importer;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.weatherDataMediator = weatherDataMediator;
        }

        private NightFrontReplanInstruction(NightFrontReplanInstruction copyMe) : this(copyMe.profileService, copyMe.importer, copyMe.safetyMonitorMediator, copyMe.weatherDataMediator) {
            CopyMetaData(copyMe);
        }

        public IList<string> Issues { get; set; } = new List<string>();

        private bool? lastRunSucceeded;

        public bool? LastRunSucceeded {
            get => lastRunSucceeded;
            private set {
                lastRunSucceeded = value;
                RaisePropertyChangedOnUIThread(nameof(LastRunSucceeded));
            }
        }

        private string statusMessage = "Not yet run";

        public string StatusMessage {
            get => statusMessage;
            private set {
                statusMessage = value;
                RaisePropertyChangedOnUIThread(nameof(StatusMessage));
            }
        }

        private DateTime? lastRunTimestamp;

        public DateTime? LastRunTimestamp {
            get => lastRunTimestamp;
            private set {
                lastRunTimestamp = value;
                RaisePropertyChangedOnUIThread(nameof(LastRunTimestamp));
            }
        }

        /// <summary>Same rationale as NightFrontUpdateInstruction's identical helper: these three
        /// properties are bound to DependencyProperties in the sequencer UI, which WPF requires to
        /// only be touched from the Dispatcher thread, but Execute runs off the UI thread.</summary>
        private void RaisePropertyChangedOnUIThread(string propertyName) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.Invoke(() => RaisePropertyChanged(propertyName));
            } else {
                RaisePropertyChanged(propertyName);
            }
        }

        public bool Validate() {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(Settings.Default.NightFrontDataFolder)) {
                issues.Add("The NightFront data folder is not configured. Set it on the NightFront plugin's Options tab.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Default.NightFrontCliPath)) {
                issues.Add("The NightFront CLI path is not configured. Set it on the NightFront plugin's Options tab.");
            }

            if (NightFrontContainer.FindAnywhere(this) == null) {
                issues.Add("Replan After Safety Recovery could not find a NightFront Container anywhere in this sequence.");
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var folder = Settings.Default.NightFrontDataFolder;
            var now = DateTime.Now;
            var todayToken = now.ToString("yyyy-MM-dd");

            var container = NightFrontContainer.FindAnywhere(this);
            if (container == null) {
                throw new NightFrontImportException("Replan After Safety Recovery could not find a NightFront Container anywhere in this sequence.");
            }

            var configPath = NightFrontMetadataPaths.GetSessionConfigPath(folder);
            if (!File.Exists(configPath)) {
                LastRunSucceeded = false;
                StatusMessage = "No session-config.json found";
                LastRunTimestamp = now;
                Notification.ShowWarning($"NightFront: no session-config.json found in '{folder}' - cannot replan. Re-export tonight's plan from NightFront (which now also writes this file) to create it.");
                return;
            }

            try {
                progress?.Report(new ApplicationStatus { Status = "NightFront: reading tonight's progress" });

                var snapshot = container.BuildProgressSnapshot();
                var matchedFile = NightFrontMetadataPaths.FindTodaysPlanFile(folder, now);
                var progressBaseName = matchedFile != null
                    ? Path.GetFileNameWithoutExtension(matchedFile)
                    : $"TargetsForTonight_{todayToken}";
                var progressPath = NightFrontMetadataPaths.GetProgressSnapshotPath(folder, progressBaseName);
                NightFrontProgressSnapshotWriter.Write(progressPath, snapshot);

                var weatherArg = WriteLiveWeatherOverrideOrNone(folder, now);

                var selectionPath = NightFrontMetadataPaths.GetSelectionPreferencePath(folder);
                var selectionArg = File.Exists(selectionPath) ? selectionPath : null;

                // Overwrite the same file NightFrontUpdateInstruction itself would look for, rather
                // than writing a parallel filename - a later import (or a second interruption later
                // the same night, whose own FindTodaysPlanFile re-discovery would otherwise find a
                // stale original) should always see the freshest plan.
                var outputPath = matchedFile ?? Path.Combine(folder, $"TargetsForTonight_{todayToken}.json");

                var arguments = BuildReplanArguments(
                    Settings.Default.ReplanEffortLevel, configPath, progressPath, weatherArg, outputPath, selectionArg);

                progress?.Report(new ApplicationStatus { Status = "NightFront: replanning the remainder of the night" });
                var (exitCode, stdErr) = await RunNightFrontCli(Settings.Default.NightFrontCliPath, arguments, token);

                if (exitCode != 0) {
                    throw new NightFrontImportException($"NightFront replan failed (exit code {exitCode}): {stdErr}");
                }

                if (!File.Exists(outputPath)) {
                    // NightFront's own replan mode exits 0 with no output file precisely when every
                    // target is already fully imaged (see Main.kt's runReplan) - not a failure, but
                    // the container must be emptied rather than left with its old contents, or
                    // "Loop while safe" restarting from the top would re-shoot a finished night.
                    container.PopulateItems(Enumerable.Empty<ISequenceItem>());
                    LastRunSucceeded = true;
                    StatusMessage = "All targets already complete - nothing to replan";
                    LastRunTimestamp = now;
                    Notification.ShowInformation("NightFront: all targets already complete tonight - nothing to replan.");
                    return;
                }

                progress?.Report(new ApplicationStatus { Status = $"NightFront: importing replanned {Path.GetFileName(outputPath)}" });
                var json = await File.ReadAllTextAsync(outputPath, token);
                var imported = importer.Import(json);

                container.PopulateItems(imported);

                LastRunSucceeded = true;
                StatusMessage = $"Replanned at {now:HH:mm} ({imported.Count} target(s))";
                LastRunTimestamp = now;
                Notification.ShowSuccess($"NightFront: replanned the remainder of the night ({imported.Count} target(s)).");
            } catch (OperationCanceledException) {
                // A user-initiated sequence stop, not a replan failure - mirrors
                // NightFrontUpdateInstruction's identical handling.
                throw;
            } catch (Exception ex) {
                LastRunSucceeded = false;
                StatusMessage = $"Replan failed: {ex.Message}";
                LastRunTimestamp = now;
                throw;
            }
        }

        /// <summary>
        /// Reads live cloud cover via IWeatherDataMediator (the same GetInfo()-based pattern
        /// NightFrontMetadataRecorder already uses for IRotatorMediator) and, if a real reading is
        /// available, writes it as a NightFront LiveWeatherOverride JSON sidecar and returns its
        /// path; otherwise returns the literal string "none" (NightFront's own CLI convention for
        /// "no live reading available" - see Main.kt's runReplan doc comment). CloudCover is only
        /// trusted when the weather device reports Connected - an unconnected device's GetInfo()
        /// still returns a WeatherDataInfo instance (never null), just with stale/default field
        /// values, which would otherwise silently look like a real 0% cloud cover reading.
        /// </summary>
        private string WriteLiveWeatherOverrideOrNone(string folder, DateTime now) {
            var info = weatherDataMediator?.GetInfo();
            if (info == null || !info.Connected || double.IsNaN(info.CloudCover)) {
                return "none";
            }

            var weatherPath = Path.Combine(folder, "live-weather-override.json");
            var json = JsonConvert.SerializeObject(new {
                cloudCoverPct = (int)Math.Round(info.CloudCover),
                asOfEpochSec = ((DateTimeOffset)now.ToUniversalTime()).ToUnixTimeSeconds(),
            });
            File.WriteAllText(weatherPath, json);
            return weatherPath;
        }

        /// <summary>
        /// Builds the full `replan` argument list NightFront's own Main.kt expects (see its
        /// REPLAN_USAGE constant: `replan [--effort=...] &lt;config&gt; &lt;progress&gt;
        /// &lt;weather|none&gt; &lt;output&gt; [selection]`) - kept as a small, pure, independently
        /// testable function rather than inlined into Execute, since Execute itself can't run
        /// outside a live NINA host.
        /// </summary>
        internal static List<string> BuildReplanArguments(
            string effortLevel, string configPath, string progressPath, string weatherArgOrNone,
            string outputPath, string selectionPathOrNull) {
            var args = new List<string> {
                "replan",
                $"--effort={effortLevel}",
                configPath,
                progressPath,
                weatherArgOrNone,
                outputPath,
            };
            if (selectionPathOrNull != null) {
                args.Add(selectionPathOrNull);
            }
            return args;
        }

        /// <summary>
        /// Spawns <paramref name="cliPath"/> with <paramref name="arguments"/>, waits up to
        /// ReplanTimeoutSeconds (or until <paramref name="token"/> is cancelled) for it to exit, and
        /// returns its exit code plus captured stderr. A timeout (as opposed to a real cancellation)
        /// kills the process tree and throws, since letting an unbounded subprocess linger would
        /// block sequence recovery indefinitely on a hung solve.
        /// </summary>
        private static async Task<(int ExitCode, string StdErr)> RunNightFrontCli(string cliPath, List<string> arguments, CancellationToken token) {
            var psi = new ProcessStartInfo {
                FileName = cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var arg in arguments) {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ReplanTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            try {
                await process.WaitForExitAsync(linkedCts.Token);
            } catch (OperationCanceledException) {
                if (token.IsCancellationRequested) {
                    throw;
                }
                TryKill(process);
                throw new NightFrontImportException($"NightFront replan timed out after {ReplanTimeoutSeconds}s.");
            }

            var stdErr = await stdErrTask;
            return (process.ExitCode, stdErr);
        }

        private static void TryKill(Process process) {
            try {
                process.Kill(entireProcessTree: true);
            } catch {
                // Best-effort - the timeout is already being reported to the caller regardless.
            }
        }

        public override object Clone() {
            return new NightFrontReplanInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontReplanInstruction)}";
        }
    }
}
