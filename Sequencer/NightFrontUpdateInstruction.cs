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

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Checks the configured NightFront data folder for a plan file matching today's date and, if
    /// found, imports it into the enclosing NightFrontContainer. Runs once per execution - the
    /// expected usage is to place this once inside a NightFrontContainer, to run right before the
    /// night's imaging sequence begins.
    /// </summary>
    [ExportMetadata("Name", "NightFront Update")]
    [ExportMetadata("Description", "Checks the configured NightFront folder for today's plan file and, if found, populates the enclosing NightFront Container with it.")]
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

        public bool Validate() {
            var issues = new List<string>();

            if (!(Parent is NightFrontContainer)) {
                issues.Add("NightFront Update must be placed directly inside a NightFront Container.");
            }

            if (string.IsNullOrWhiteSpace(Settings.Default.NightFrontDataFolder)) {
                issues.Add("The NightFront data folder is not configured. Set it on the NightFront plugin's Options tab.");
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var folder = Settings.Default.NightFrontDataFolder;
            var todayToken = DateTime.Now.ToString("yyyy-MM-dd");

            string matchedFile = null;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) {
                matchedFile = Directory.EnumerateFiles(folder, "*.json")
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(todayToken));
            }

            if (matchedFile == null) {
                Notification.ShowInformation($"NightFront: no plan file found for {todayToken} in '{folder}'. Continuing without update.");
                return;
            }

            progress?.Report(new ApplicationStatus { Status = $"NightFront: reading {Path.GetFileName(matchedFile)}" });
            var json = await File.ReadAllTextAsync(matchedFile, token);

            progress?.Report(new ApplicationStatus { Status = "NightFront: importing plan" });
            var imported = importer.Import(json);

            if (!(Parent is NightFrontContainer container)) {
                throw new NightFrontImportException("NightFront Update could not find its parent NightFront Container.");
            }

            container.PopulateItems(imported);
            Notification.ShowSuccess($"NightFront: imported plan for {todayToken} ({imported.Count} target(s)).");
        }

        public override object Clone() {
            return new NightFrontUpdateInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontUpdateInstruction)}";
        }
    }
}
