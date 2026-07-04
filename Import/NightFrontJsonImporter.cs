using Newtonsoft.Json.Linq;
using NINA.Astrometry;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Astrometry.Interfaces;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Autofocus;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Guider;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.Core.Utility.WindowService;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Walks a NightFront-exported plan JSON (a hand-rolled mimic of NINA's own native sequence
    /// serialization shape) and constructs the real, live NINA.Sequencer instruction/container objects
    /// it describes. NINA's own JSON sequence deserializer (SequenceJsonConverter/TemplateController)
    /// is internal to NINA's composition root and unavailable to plugins, so this importer builds the
    /// closed set of instruction types NightFront actually emits directly, mirroring the approach the
    /// tcpalmer/nina.plugin.targetscheduler plugin uses for its own externally-sourced plan data.
    /// </summary>
    public class NightFrontJsonImporter {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IAutoFocusVMFactory autoFocusVMFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IWindowServiceFactory windowServiceFactory;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IApplicationMediator applicationMediator;
        private readonly IPlanetariumFactory planetariumFactory;
        private readonly IList<IDateTimeProvider> dateTimeProviders;

        public NightFrontJsonImporter(
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
            IList<IDateTimeProvider> dateTimeProviders) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.focuserMediator = focuserMediator;
            this.autoFocusVMFactory = autoFocusVMFactory;
            this.telescopeMediator = telescopeMediator;
            this.rotatorMediator = rotatorMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            this.nighttimeCalculator = nighttimeCalculator;
            this.framingAssistantVM = framingAssistantVM;
            this.applicationMediator = applicationMediator;
            this.planetariumFactory = planetariumFactory;
            this.dateTimeProviders = dateTimeProviders;
        }

        /// <summary>
        /// Parses a NightFront plan JSON string (root: a SequentialContainer named "TargetsForTonight")
        /// and returns the flattened list of per-target DeepSkyObjectContainers it contains, ready to be
        /// added directly to a NightFrontContainer's Items.
        /// </summary>
        public IList<ISequenceItem> Import(string json) {
            JObject root;
            try {
                root = JObject.Parse(json);
            } catch (Exception ex) {
                throw new NightFrontImportException("NightFront plan file is not valid JSON.", ex);
            }

            var rootType = GetTypeShortName(root["$type"]);
            if (rootType != "SequentialContainer") {
                throw new NightFrontImportException($"Expected the NightFront plan's root $type to be SequentialContainer, but found '{rootType}'.");
            }

            var items = new List<ISequenceItem>();
            foreach (var node in GetValuesArray(root["Items"])) {
                items.Add(BuildItem(node));
            }
            return items;
        }

        private ISequenceItem BuildItem(JToken node) {
            var type = GetTypeShortName(node["$type"]);
            switch (type) {
                case "DeepSkyObjectContainer": return BuildDeepSkyObjectContainer(node);
                case "SequentialContainer": return BuildSequentialContainer(node);
                case "SwitchFilter": return BuildSwitchFilter(node);
                case "TakeExposure": return BuildTakeExposure(node);
                case "Dither": return BuildDither(node);
                case "RunAutofocus": return BuildRunAutofocus(node);
                case "StartGuiding": return BuildStartGuiding(node);
                case "CenterAndRotate": return BuildCenterAndRotate(node);
                default:
                    throw new NightFrontImportException($"Unsupported instruction type '{type}' encountered while importing NightFront plan.");
            }
        }

        private ISequenceCondition BuildCondition(JToken node) {
            var type = GetTypeShortName(node["$type"]);
            switch (type) {
                case "TimeCondition": return BuildTimeCondition(node);
                case "LoopCondition": return BuildLoopCondition(node);
                default:
                    throw new NightFrontImportException($"Unsupported condition type '{type}' encountered while importing NightFront plan.");
            }
        }

        private DeepSkyObjectContainer BuildDeepSkyObjectContainer(JToken node) {
            var container = new DeepSkyObjectContainer(profileService, nighttimeCalculator, framingAssistantVM, applicationMediator, planetariumFactory, cameraMediator, filterWheelMediator) {
                Name = node["Name"]?.Value<string>() ?? "Target"
            };

            var targetNode = node["Target"];
            if (targetNode != null) {
                container.Target = BuildInputTarget(targetNode);
            }

            foreach (var childNode in GetValuesArray(node["Items"])) {
                container.Add(BuildItem(childNode));
            }

            return container;
        }

        private SequentialContainer BuildSequentialContainer(JToken node) {
            var container = new SequentialContainer {
                Name = node["Name"]?.Value<string>() ?? string.Empty
            };

            foreach (var conditionNode in GetValuesArray(node["Conditions"])) {
                container.Add(BuildCondition(conditionNode));
            }

            foreach (var childNode in GetValuesArray(node["Items"])) {
                container.Add(BuildItem(childNode));
            }

            return container;
        }

        private InputTarget BuildInputTarget(JToken node) {
            var astrometry = profileService.ActiveProfile.AstrometrySettings;
            var target = new InputTarget(Angle.ByDegree(astrometry.Latitude), Angle.ByDegree(astrometry.Longitude), astrometry.Horizon) {
                TargetName = node["TargetName"]?.Value<string>() ?? string.Empty,
                PositionAngle = node["PositionAngle"]?.Value<double>() ?? 0d
            };

            var coordinatesNode = node["InputCoordinates"];
            if (coordinatesNode != null) {
                target.InputCoordinates = BuildInputCoordinates(coordinatesNode);
            }

            return target;
        }

        private static InputCoordinates BuildInputCoordinates(JToken node) {
            return new InputCoordinates {
                RAHours = node["RAHours"]?.Value<int>() ?? 0,
                RAMinutes = node["RAMinutes"]?.Value<int>() ?? 0,
                RASeconds = node["RASeconds"]?.Value<double>() ?? 0d,
                NegativeDec = node["NegativeDec"]?.Value<bool>() ?? false,
                DecDegrees = node["DecDegrees"]?.Value<int>() ?? 0,
                DecMinutes = node["DecMinutes"]?.Value<int>() ?? 0,
                DecSeconds = node["DecSeconds"]?.Value<double>() ?? 0d
            };
        }

        private TimeCondition BuildTimeCondition(JToken node) {
            return new TimeCondition(dateTimeProviders) {
                Hours = node["Hours"]?.Value<int>() ?? 0,
                Minutes = node["Minutes"]?.Value<int>() ?? 0,
                Seconds = node["Seconds"]?.Value<int>() ?? 0,
                MinutesOffset = node["MinutesOffset"]?.Value<int>() ?? 0
            };
        }

        private static LoopCondition BuildLoopCondition(JToken node) {
            return new LoopCondition {
                Iterations = node["Iterations"]?.Value<int>() ?? 1
            };
        }

        private SwitchFilter BuildSwitchFilter(JToken node) {
            var filterNode = node["Filter"];
            var filterName = filterNode?["_name"]?.Value<string>();
            if (string.IsNullOrEmpty(filterName)) {
                throw new NightFrontImportException("A SwitchFilter step in the NightFront plan is missing a filter name.");
            }

            var liveFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters
                .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));
            if (liveFilter == null) {
                throw new NightFrontImportException($"NightFront plan references filter '{filterName}', which is not present in the currently configured filter wheel.");
            }

            return new SwitchFilter(profileService, filterWheelMediator) {
                Filter = liveFilter
            };
        }

        private TakeExposure BuildTakeExposure(JToken node) {
            var binningNode = node["Binning"];
            var binningX = (short)(binningNode?["X"]?.Value<int>() ?? 1);
            var binningY = (short)(binningNode?["Y"]?.Value<int>() ?? 1);

            return new TakeExposure(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
                ExposureTime = node["ExposureTime"]?.Value<double>() ?? 0d,
                Gain = node["Gain"]?.Value<int>() ?? -1,
                Offset = node["Offset"]?.Value<int>() ?? -1,
                Binning = new BinningMode(binningX, binningY),
                ImageType = node["ImageType"]?.Value<string>() ?? "LIGHT",
                ExposureCount = node["ExposureCount"]?.Value<int>() ?? 0
            };
        }

        private Dither BuildDither(JToken node) {
            return new Dither(guiderMediator, profileService);
        }

        private RunAutofocus BuildRunAutofocus(JToken node) {
            return new RunAutofocus(profileService, imageHistoryVM, cameraMediator, filterWheelMediator, focuserMediator, autoFocusVMFactory);
        }

        private StartGuiding BuildStartGuiding(JToken node) {
            return new StartGuiding(guiderMediator) {
                ForceCalibration = node["ForceCalibration"]?.Value<bool>() ?? false
            };
        }

        private CenterAndRotate BuildCenterAndRotate(JToken node) {
            var coordinatesNode = node["Coordinates"];
            return new CenterAndRotate(profileService, telescopeMediator, imagingMediator, rotatorMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory) {
                PositionAngle = node["PositionAngle"]?.Value<double>() ?? 0d,
                Coordinates = coordinatesNode != null ? BuildInputCoordinates(coordinatesNode) : new InputCoordinates()
            };
        }

        private static JArray GetValuesArray(JToken collectionToken) {
            return collectionToken?["$values"] as JArray ?? new JArray();
        }

        private static string GetTypeShortName(JToken typeToken) {
            var full = typeToken?.Value<string>();
            if (string.IsNullOrEmpty(full)) {
                throw new NightFrontImportException("Encountered a JSON node without a $type discriminator while importing NightFront plan.");
            }
            var beforeComma = full.Split(',')[0].Trim();
            var lastDot = beforeComma.LastIndexOf('.');
            return lastDot >= 0 ? beforeComma.Substring(lastDot + 1) : beforeComma;
        }
    }
}
