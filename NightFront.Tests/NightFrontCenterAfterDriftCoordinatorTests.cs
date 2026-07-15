using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.Platesolving;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Builds test object graphs by running real plan JSON through NightFrontJsonImporter (same
    // approach as NightFrontMetadataRecorderTests/NightFrontJsonImporterTests) - required so
    // DeepSkyObjectContainer.Target.InputCoordinates is a real, populated InputCoordinates rather
    // than a mock, since the coordinator reads real RA/Dec off of it to push into the trigger.
    public class NightFrontCenterAfterDriftCoordinatorTests {

        // decDegrees is SIGNED (e.g. -10 for 10 degrees south). NINA's own InputCoordinates
        // reconstructs the numeric Dec as `DecDegrees - DecMinutes/60 - DecSeconds/3600` using
        // DecDegrees' own sign directly - NegativeDec only controls the DMS string's displayed "-"
        // prefix, not the reconstructed value - so DecDegrees itself must carry the sign (see
        // CLAUDE.md's "BUG FIX: NinaSequenceModel.inputCoordinates() wrote DecDegrees as a
        // non-negative magnitude" for the identical lesson learned on the exporter's write side).
        private static string BuildTargetJson(string name, double raHours, double decDegrees) {
            var negativeDec = decDegrees < 0;
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
              ""Name"": ""{name}"",
              ""Target"": {{
                ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
                ""TargetName"": ""{name}"",
                ""PositionAngle"": 0,
                ""InputCoordinates"": {{
                  ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
                  ""RAHours"": {raHours}, ""RAMinutes"": 0, ""RASeconds"": 0,
                  ""NegativeDec"": {negativeDec.ToString().ToLowerInvariant()}, ""DecDegrees"": {decDegrees}, ""DecMinutes"": 0, ""DecSeconds"": 0
                }}
              }},
              ""Items"": {{
                ""$values"": [
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": 0 }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""Ha"" }} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""Gain"": -1, ""Offset"": -1 }}
                ]
              }}
            }}";
        }

        private static string BuildPlanJson(params string[] targetsJson) {
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
              ""Items"": {{ ""$values"": [ {string.Join(",", targetsJson)} ] }}
            }}";
        }

        private static NightFrontJsonImporter CreateImporter(IProfileService profileService) {
            // Several imported item types call their own Validate() internally from
            // AfterParentChanged() - unprotected by SequenceContainer.AfterParentChanged's separate
            // try/catch around a validatable's Validate() call, which only guards a *second*, later
            // call, not this first internal one - the moment the importer attaches them to their
            // parent (i.e. during Import() itself, not lazily): CenterAndRotate needs
            // telescopeMediator/rotatorMediator.GetInfo().Connected, SwitchFilter needs
            // filterWheelMediator.GetInfo().Connected, and TakeExposure needs
            // cameraMediator.GetInfo().Connected. A bare Mock.Of<T>() leaves GetInfo() returning
            // null, which throws a NullReferenceException the instant any target using these item
            // types is imported - so, unlike the mediators below with no such eagerly-called
            // Validate(), these four need a real (if minimal) GetInfo() stub.
            var telescopeMediator = new Mock<ITelescopeMediator>();
            telescopeMediator.Setup(m => m.GetInfo()).Returns(new TelescopeInfo { Connected = true });
            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(m => m.GetInfo()).Returns(new RotatorInfo { Connected = true });
            var filterWheelMediator = new Mock<IFilterWheelMediator>();
            filterWheelMediator.Setup(m => m.GetInfo()).Returns(new FilterWheelInfo { Connected = true });
            var cameraMediator = new Mock<ICameraMediator>();
            cameraMediator.Setup(m => m.GetInfo()).Returns(new CameraInfo { Connected = true });

            return new NightFrontJsonImporter(
                profileService,
                cameraMediator.Object,
                Mock.Of<IImagingMediator>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IImageHistoryVM>(),
                filterWheelMediator.Object,
                Mock.Of<IGuiderMediator>(),
                Mock.Of<IFocuserMediator>(),
                Mock.Of<IAutoFocusVMFactory>(),
                telescopeMediator.Object,
                rotatorMediator.Object,
                Mock.Of<IDomeMediator>(),
                Mock.Of<IDomeFollower>(),
                Mock.Of<IPlateSolverFactory>(),
                Mock.Of<IWindowServiceFactory>(),
                Mock.Of<INighttimeCalculator>(),
                Mock.Of<IFramingAssistantVM>(),
                Mock.Of<IApplicationMediator>(),
                Mock.Of<IPlanetariumFactory>(),
                new List<IDateTimeProvider>());
        }

        private static IProfileService CreateProfileServiceWithFilters(params string[] filterNames) {
            var filters = filterNames.Select(name => new FilterInfo(name, 0, 0)).ToArray();

            var astrometrySettings = new Mock<IAstrometrySettings>();
            astrometrySettings.SetupGet(x => x.Latitude).Returns(45.0);
            astrometrySettings.SetupGet(x => x.Longitude).Returns(-93.0);
            astrometrySettings.SetupGet(x => x.Horizon).Returns((CustomHorizon)null!);

            var filterWheelSettings = new Mock<IFilterWheelSettings>();
            filterWheelSettings.SetupGet(x => x.FilterWheelFilters).Returns(new ObserveAllCollection<FilterInfo>(filters));

            // TakeExposure.Validate() (called eagerly from AfterParentChanged - see CreateImporter's
            // own comment) dereferences profileService.ActiveProfile.ImageFileSettings.FilePath
            // unconditionally, so ImageFileSettings itself must be non-null even though FilePath can
            // stay null (IsNullOrWhiteSpace handles that safely).
            var imageFileSettings = new Mock<IImageFileSettings>();

            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettings.Object);
            profile.SetupGet(x => x.FilterWheelSettings).Returns(filterWheelSettings.Object);
            profile.SetupGet(x => x.ImageFileSettings).Returns(imageFileSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);

            return profileService.Object;
        }

        private static List<DeepSkyObjectContainer> FindDsoContainers(IEnumerable<ISequenceItem> items) {
            return items.OfType<DeepSkyObjectContainer>().ToList();
        }

        private static CenterAndRotate FindCenterAndRotate(ISequenceContainer container) {
            foreach (var item in container.Items) {
                if (item is CenterAndRotate centerAndRotate) {
                    return centerAndRotate;
                }
                if (item is ISequenceContainer nested) {
                    var found = FindCenterAndRotate(nested);
                    if (found != null) {
                        return found;
                    }
                }
            }
            return null;
        }

        private static CenterAfterDriftTrigger CreateCenterAfterDriftTrigger() {
            return new CenterAfterDriftTrigger(
                Mock.Of<IProfileService>(),
                Mock.Of<ITelescopeMediator>(),
                Mock.Of<IFilterWheelMediator>(),
                Mock.Of<IGuiderMediator>(),
                Mock.Of<IImagingMediator>(),
                Mock.Of<ICameraMediator>(),
                Mock.Of<IDomeMediator>(),
                Mock.Of<IDomeFollower>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IApplicationStatusMediator>());
        }

        /// <summary>Builds a mocked ancestor container whose GetTriggersSnapshot() returns
        /// <paramref name="triggers"/> - standing in for whatever branch of the user's own template
        /// (e.g. "Loop while safe") a CenterAfterDriftTrigger is actually attached to, several levels
        /// above the NightFront Container.</summary>
        private static Mock<ISequenceContainer> MockTriggerableAncestor(ISequenceContainer parent, params ISequenceTrigger[] triggers) {
            var mock = new Mock<ISequenceContainer>();
            mock.Setup(c => c.Parent).Returns(parent);
            mock.As<ITriggerable>().Setup(t => t.GetTriggersSnapshot()).Returns(triggers);
            return mock;
        }

        /// <summary>An intermediate ancestor with no triggers at all - used to confirm the coordinator
        /// keeps walking upward past it rather than stopping/throwing.</summary>
        private static Mock<ISequenceContainer> MockNonTriggerableAncestor(ISequenceContainer parent) {
            var mock = new Mock<ISequenceContainer>();
            mock.Setup(c => c.Parent).Returns(parent);
            return mock;
        }

        [Fact]
        public void PushesLiveTargetCoordinates_WhenCenterAndRotateStartsRunning() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", raHours: 10, decDegrees: 20)));

            var trigger = CreateCenterAfterDriftTrigger();
            var ancestor = MockTriggerableAncestor(null, trigger);

            var nightFrontContainer = new NightFrontContainer();
            nightFrontContainer.AttachNewParent(ancestor.Object);
            nightFrontContainer.PopulateItems(imported);

            new NightFrontCenterAfterDriftCoordinator(nightFrontContainer, imported);

            Assert.False(trigger.Inherited, "must not push anything before the target actually starts running");

            var centerAndRotate = FindCenterAndRotate(nightFrontContainer);
            Assert.NotNull(centerAndRotate);
            centerAndRotate.Status = SequenceEntityStatus.RUNNING;

            Assert.True(trigger.Inherited);
            Assert.Equal(150.0, trigger.Coordinates.Coordinates.RADegrees, 3); // 10h -> 150 deg
            Assert.Equal(20.0, trigger.Coordinates.Coordinates.Dec, 3);
        }

        [Fact]
        public void DoesNotPushCoordinates_BeforeCenterAndRotateStarts() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", raHours: 10, decDegrees: 20)));

            var trigger = CreateCenterAfterDriftTrigger();
            var ancestor = MockTriggerableAncestor(null, trigger);

            var nightFrontContainer = new NightFrontContainer();
            nightFrontContainer.AttachNewParent(ancestor.Object);
            nightFrontContainer.PopulateItems(imported);

            new NightFrontCenterAfterDriftCoordinator(nightFrontContainer, imported);

            Assert.False(trigger.Inherited);
        }

        [Fact]
        public void PushesEachTargetsOwnCoordinates_AsControlMovesFromOneTargetToTheNext() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(
                BuildTargetJson("Target A", raHours: 10, decDegrees: 20),
                BuildTargetJson("Target B", raHours: 5, decDegrees: -10)));

            var trigger = CreateCenterAfterDriftTrigger();
            var ancestor = MockTriggerableAncestor(null, trigger);

            var nightFrontContainer = new NightFrontContainer();
            nightFrontContainer.AttachNewParent(ancestor.Object);
            nightFrontContainer.PopulateItems(imported);

            new NightFrontCenterAfterDriftCoordinator(nightFrontContainer, imported);

            var dsoContainers = FindDsoContainers(nightFrontContainer.Items);
            Assert.Equal(2, dsoContainers.Count);

            FindCenterAndRotate(dsoContainers[0]).Status = SequenceEntityStatus.RUNNING;
            Assert.Equal(150.0, trigger.Coordinates.Coordinates.RADegrees, 3);
            Assert.Equal(20.0, trigger.Coordinates.Coordinates.Dec, 3);

            FindCenterAndRotate(dsoContainers[1]).Status = SequenceEntityStatus.RUNNING;
            Assert.Equal(75.0, trigger.Coordinates.Coordinates.RADegrees, 3); // 5h -> 75 deg
            Assert.Equal(-10.0, trigger.Coordinates.Coordinates.Dec, 3);
        }

        [Fact]
        public void SearchesPastNonTriggerableAncestors_ToFindTheTriggerFartherUp() {
            // Mirrors the maintainer's real production template: the trigger is attached several
            // levels above the NightFront Container (e.g. "Loop while safe"), not its immediate
            // parent - the coordinator must keep walking upward, not stop at the first ancestor.
            var trigger = CreateCenterAfterDriftTrigger();
            var loopWhileSafe = MockTriggerableAncestor(null, trigger);
            var intermediate = MockNonTriggerableAncestor(loopWhileSafe.Object);

            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", raHours: 10, decDegrees: 20)));

            var nightFrontContainer = new NightFrontContainer();
            nightFrontContainer.AttachNewParent(intermediate.Object);
            nightFrontContainer.PopulateItems(imported);

            new NightFrontCenterAfterDriftCoordinator(nightFrontContainer, imported);

            FindCenterAndRotate(nightFrontContainer).Status = SequenceEntityStatus.RUNNING;

            Assert.True(trigger.Inherited);
            Assert.Equal(150.0, trigger.Coordinates.Coordinates.RADegrees, 3);
        }

        [Fact]
        public void DoesNothing_WhenNoCenterAfterDriftTriggerExistsAnywhereInTheAncestryChain() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", raHours: 10, decDegrees: 20)));

            // No triggers anywhere in the (mocked) ancestry - the coordinator must not throw.
            var ancestor = MockTriggerableAncestor(null);

            var nightFrontContainer = new NightFrontContainer();
            nightFrontContainer.AttachNewParent(ancestor.Object);
            nightFrontContainer.PopulateItems(imported);

            new NightFrontCenterAfterDriftCoordinator(nightFrontContainer, imported);

            var centerAndRotate = FindCenterAndRotate(nightFrontContainer);
            var exception = Record.Exception(() => centerAndRotate.Status = SequenceEntityStatus.RUNNING);

            Assert.Null(exception);
        }
    }
}
