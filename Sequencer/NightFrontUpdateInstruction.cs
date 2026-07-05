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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Checks the configured NightFront data folder for a plan file matching today's date and, if
    /// found, imports it into the NightFront Container that immediately follows it. Runs once per
    /// execution - the expected usage is to place this once, immediately before a NightFront
    /// Container (as its preceding sibling), to run right before the night's imaging sequence
    /// begins.
    /// </summary>
    [ExportMetadata("Name", "Nightly Update")]
    [ExportMetadata("Description", "Checks the configured NightFront folder for today's plan file and, if found, populates the following NightFront Container with it.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontUpdateInstruction : SequenceItem, IValidatable {
        private readonly IProfileService profileService;
        private readonly NightFrontJsonImporter importer;

        [ImportingConstructor]
        public NightFrontUpdateInstruction(
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
            IList<IDateTimeProvider> dateTimeProviders)
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
                    dateTimeProviders)) {
        }

        private NightFrontUpdateInstruction(IProfileService profileService, NightFrontJsonImporter importer) {
            this.profileService = profileService;
            this.importer = importer;
        }

        private NightFrontUpdateInstruction(NightFrontUpdateInstruction copyMe) : this(copyMe.profileService, copyMe.importer) {
            CopyMetaData(copyMe);
        }

        public IList<string> Issues { get; set; } = new List<string>();

        private bool? lastRunSucceeded;

        /// <summary>
        /// Null if this instruction has never run; otherwise whether its last run successfully
        /// imported a plan. Drives the status indicator in the sequencer UI.
        /// </summary>
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

        /// <summary>
        /// NINA executes sequence items off the UI thread, but these three properties are bound to
        /// DependencyProperties (Ellipse.Fill, TextBlock.Text) in the sequencer UI, which WPF
        /// requires to only be touched from the Dispatcher thread.
        /// </summary>
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

            if (Parent == null || NightFrontContainer.FindNext(Parent.Items, this) == null) {
                issues.Add("Nightly Update must be immediately followed, as a sibling in the same container, by a NightFront Container.");
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var folder = Settings.Default.NightFrontDataFolder;
            var todayToken = DateTime.Now.ToString("yyyy-MM-dd");

            string matchedFile = null;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) {
                // Exclude the .metadata.json sidecar WriteMetadataFile writes into this same folder -
                // its name is derived from the plan file's and so also contains todayToken, and would
                // otherwise be a candidate match on a same-day re-run.
                matchedFile = Directory.EnumerateFiles(folder, "*.json")
                    .Where(f => !f.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(todayToken));
            }

            if (matchedFile == null) {
                LastRunSucceeded = false;
                StatusMessage = "Plan not found";
                LastRunTimestamp = DateTime.Now;
                Notification.ShowInformation($"NightFront: no plan file found for {todayToken} in '{folder}'. Continuing without update.");
                return;
            }

            try {
                progress?.Report(new ApplicationStatus { Status = $"NightFront: reading {Path.GetFileName(matchedFile)}" });
                var json = await File.ReadAllTextAsync(matchedFile, token);

                progress?.Report(new ApplicationStatus { Status = "NightFront: importing plan" });
                var imported = importer.Import(json);

                var container = NightFrontContainer.FindNext(Parent.Items, this);
                if (container == null) {
                    throw new NightFrontImportException("Nightly Update could not find a following NightFront Container.");
                }

                container.PopulateItems(imported);

                LastRunSucceeded = true;
                StatusMessage = $"Plan retrieved from file: {Path.GetFileName(matchedFile)}";
                LastRunTimestamp = DateTime.Now;

                WriteMetadataFile(folder, matchedFile, imported);

                Notification.ShowSuccess($"NightFront: imported plan for {todayToken} ({imported.Count} target(s)).");
            } catch (OperationCanceledException) {
                // A user-initiated sequence stop, not an import failure - leave the status indicator
                // showing whatever it last reported rather than overwriting it with a false failure
                // message. NINA's own engine handles the cancelled state.
                throw;
            } catch (Exception ex) {
                // Catch broadly, not just NightFrontImportException: the status indicator's whole
                // purpose is to reliably reflect what happened, so an unanticipated failure here
                // must still flip it to the red/failure state rather than leaving it stuck on
                // whatever it showed before this run.
                LastRunSucceeded = false;
                StatusMessage = $"Plan import failed: {ex.Message}";
                LastRunTimestamp = DateTime.Now;
                throw;
            }
        }

        private static void WriteMetadataFile(string folder, string matchedFile, IList<ISequenceItem> imported) {
            try {
                var metadata = NightFrontPlanMetadataExtractor.Extract(imported, Path.GetFileName(matchedFile));
                var metadataPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(matchedFile) + ".metadata.json");
                File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: failed to write calibration metadata file: {ex.Message}");
            }
        }

        public override object Clone() {
            return new NightFrontUpdateInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontUpdateInstruction)}";
        }
    }
}
