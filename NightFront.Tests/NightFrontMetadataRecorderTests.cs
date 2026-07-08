using JeffRidder.NINA.Nightfront.Import;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.Core.Utility.WindowService;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Builds test object graphs by running real plan JSON through NightFrontJsonImporter (same
    // approach as NightFrontJsonImporterTests). These tests populate InputCoordinates on a real
    // InputTarget (required for CenterAndRotate to attach to its parent without throwing), which
    // transitively calls into NOVAS31lib.dll - see NightFrontJsonImporterTests' class comment.
    public class NightFrontMetadataRecorderTests {

        private static string BuildTargetJson(string name, double positionAngle, string filter, int gain = -1, int offset = -1) {
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
              ""Name"": ""{name}"",
              ""Target"": {{
                ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
                ""TargetName"": ""{name}"",
                ""PositionAngle"": {positionAngle},
                ""InputCoordinates"": {{
                  ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
                  ""RAHours"": 20, ""RAMinutes"": 59, ""RASeconds"": 17.1,
                  ""NegativeDec"": false, ""DecDegrees"": 44, ""DecMinutes"": 31, ""DecSeconds"": 44.0
                }}
              }},
              ""Items"": {{
                ""$values"": [
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": {positionAngle} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""{filter}"" }} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""Gain"": {gain}, ""Offset"": {offset} }}
                ]
              }}
            }}";
        }

        // A target with two filter/exposure blocks (used by the gain/offset-attribution test), so a
        // TakeExposure is always paired with the SwitchFilter that most recently preceded it.
        private static string BuildTwoFilterBlocksTargetJson(string name, double positionAngle, string filter, int gain1, int offset1, int gain2, int offset2) {
            return $@"{{
              ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
              ""Name"": ""{name}"",
              ""Target"": {{
                ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
                ""TargetName"": ""{name}"",
                ""PositionAngle"": {positionAngle},
                ""InputCoordinates"": {{
                  ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
                  ""RAHours"": 20, ""RAMinutes"": 59, ""RASeconds"": 17.1,
                  ""NegativeDec"": false, ""DecDegrees"": 44, ""DecMinutes"": 31, ""DecSeconds"": 44.0
                }}
              }},
              ""Items"": {{
                ""$values"": [
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": {positionAngle} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""{filter}"" }} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""Gain"": {gain1}, ""Offset"": {offset1} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""{filter}"" }} }},
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""Gain"": {gain2}, ""Offset"": {offset2} }}
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
            return new NightFrontJsonImporter(
                profileService,
                Mock.Of<ICameraMediator>(),
                Mock.Of<IImagingMediator>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IImageHistoryVM>(),
                Mock.Of<IFilterWheelMediator>(),
                Mock.Of<IGuiderMediator>(),
                Mock.Of<IFocuserMediator>(),
                Mock.Of<IAutoFocusVMFactory>(),
                Mock.Of<ITelescopeMediator>(),
                Mock.Of<IRotatorMediator>(),
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

            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettings.Object);
            profile.SetupGet(x => x.FilterWheelSettings).Returns(filterWheelSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);

            return profileService.Object;
        }

        private static CenterAndRotate? FindCenterAndRotate(IEnumerable<ISequenceItem> items) {
            foreach (var item in items) {
                if (item is CenterAndRotate car) {
                    return car;
                }
                if (item is ISequenceContainer container) {
                    var found = FindCenterAndRotate(container.Items);
                    if (found != null) {
                        return found;
                    }
                }
            }
            return null;
        }

        private static List<TakeExposure> FindTakeExposures(IEnumerable<ISequenceItem> items) {
            var results = new List<TakeExposure>();
            foreach (var item in items) {
                if (item is TakeExposure exposure) {
                    results.Add(exposure);
                }
                if (item is ISequenceContainer container) {
                    results.AddRange(FindTakeExposures(container.Items));
                }
            }
            return results;
        }

        private static string CreateTempPath() {
            return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
        }

        [Fact]
        public void Extract_DoesNotRecordAnythingUntilCenterAndRotateFinishes() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", 12.5, "Ha")));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 99f });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);

                Assert.False(File.Exists(livePath));
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_DoesNotRecordAnythingUntilAnExposureFinishes_EvenAfterCenterAndRotateFinishes() {
            // Regression guard for the bug where metadata was written as soon as CenterAndRotate
            // finished - before any light exposure had actually completed.
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", 12.5, "Ha")));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 99f });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);

                var centerAndRotate = FindCenterAndRotate(imported);
                Assert.NotNull(centerAndRotate);
                centerAndRotate.Status = SequenceEntityStatus.FINISHED;

                Assert.False(File.Exists(livePath));
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_RecordsRotatorMediatorMeasuredAngle_NotThePlanInputAngle() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            // Plan's input Sky PA is 12.5 - the recorder must ignore this and use the mocked
            // rotator's measured MechanicalPosition (99) instead.
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", 12.5, "Ha")));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 99f });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);
                var centerAndRotate = FindCenterAndRotate(imported);
                Assert.NotNull(centerAndRotate);
                centerAndRotate.Status = SequenceEntityStatus.FINISHED;
                var exposure = Assert.Single(FindTakeExposures(imported));
                exposure.Status = SequenceEntityStatus.FINISHED;

                var metadata = NightFrontMetadataStore.Load(livePath);

                var requirement = Assert.Single(metadata.CalibrationRequirements);
                Assert.Equal("Ha", requirement.Filter);
                Assert.Equal(99.0, requirement.RotationAngle, 3);
                Assert.NotEqual(12.5, requirement.RotationAngle);
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_RecordsOnlyTheFilterWhoseExposureFinished_WhenAnotherFilterHasNot() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(
                BuildTwoFilterBlocksTargetJson("NGC 7000", 12.5, "Ha", gain1: 100, offset1: 10, gain2: 200, offset2: 20)));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 30.0f });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);

                var exposures = FindTakeExposures(imported);
                Assert.Equal(2, exposures.Count);
                exposures[0].Status = SequenceEntityStatus.FINISHED;

                var metadata = NightFrontMetadataStore.Load(livePath);
                var requirement = Assert.Single(metadata.CalibrationRequirements);
                Assert.Equal(100, requirement.Gain);
                Assert.Equal(10, requirement.Offset);

                // The second filter block's exposure hasn't finished yet - finishing it now should
                // add its own requirement without disturbing the first.
                exposures[1].Status = SequenceEntityStatus.FINISHED;

                metadata = NightFrontMetadataStore.Load(livePath);
                Assert.Equal(2, metadata.CalibrationRequirements.Count);
                Assert.Contains(metadata.CalibrationRequirements, r => r.Gain == 200 && r.Offset == 20);
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_DedupesSameFilterGainOffsetWithinOneDegree_AcrossTargets() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            // Both targets use the default sentinel gain/offset (-1,-1) from BuildTargetJson, so this
            // also exercises that two targets producing the identical (filter, angle, gain, offset)
            // tuple in one run resolve to exactly one stored requirement rather than two.
            var imported = importer.Import(BuildPlanJson(
                BuildTargetJson("NGC 7000", 12.5, "Ha"),
                BuildTargetJson("M31", 45.0, "Ha")));

            var angles = new Queue<float>(new[] { 30.0f, 30.4f }); // within 1 degree of each other
            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(() => new RotatorInfo { MechanicalPosition = angles.Dequeue() });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);

                foreach (var exposure in FindTakeExposures(imported)) {
                    exposure.Status = SequenceEntityStatus.FINISHED;
                }

                var metadata = NightFrontMetadataStore.Load(livePath);
                Assert.Single(metadata.CalibrationRequirements);
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_DoesNotDedupeSameFilterMoreThanOneDegreeApart() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(
                BuildTargetJson("NGC 7000", 12.5, "Ha"),
                BuildTargetJson("M31", 45.0, "Ha")));

            var angles = new Queue<float>(new[] { 30.0f, 35.0f }); // more than 1 degree apart
            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(() => new RotatorInfo { MechanicalPosition = angles.Dequeue() });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);

                foreach (var exposure in FindTakeExposures(imported)) {
                    exposure.Status = SequenceEntityStatus.FINISHED;
                }

                var metadata = NightFrontMetadataStore.Load(livePath);
                Assert.Equal(2, metadata.CalibrationRequirements.Count);
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_DoesNotDedupeSameFilterAngle_WhenGainOrOffsetDiffers() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            // Same target, same rotation angle, same filter - but two exposure blocks with different
            // gain/offset. Item 0's core new behavior: gain/offset join the dedup key, so this must
            // produce two distinct calibration requirements, not one.
            var imported = importer.Import(BuildPlanJson(
                BuildTwoFilterBlocksTargetJson("NGC 7000", 12.5, "Ha", gain1: 100, offset1: 10, gain2: 200, offset2: 20)));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 30.0f });

            var livePath = CreateTempPath();
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", livePath);
                foreach (var exposure in FindTakeExposures(imported)) {
                    exposure.Status = SequenceEntityStatus.FINISHED;
                }

                var metadata = NightFrontMetadataStore.Load(livePath);
                Assert.Equal(2, metadata.CalibrationRequirements.Count);
                Assert.Contains(metadata.CalibrationRequirements, r => r.Gain == 100 && r.Offset == 10);
                Assert.Contains(metadata.CalibrationRequirements, r => r.Gain == 200 && r.Offset == 20);
            } finally {
                File.Delete(livePath);
            }
        }

        [Fact]
        public void Extract_AccumulatesAcrossConstructions_DoesNotOverwritePriorEntries() {
            var profileService = CreateProfileServiceWithFilters("Ha", "OIII");
            var importer = CreateImporter(profileService);
            var livePath = CreateTempPath();

            try {
                // Simulates a prior night: a first recorder records one requirement and is discarded.
                var firstNight = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", 12.5, "Ha")));
                var firstRotator = new Mock<IRotatorMediator>();
                firstRotator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 30.0f });
                new NightFrontMetadataRecorder(firstNight, firstRotator.Object, "plan-night1.json", livePath);
                foreach (var exposure in FindTakeExposures(firstNight)) {
                    exposure.Status = SequenceEntityStatus.FINISHED;
                }

                // A second, independent recorder construction against the same live path (a later
                // night) must accumulate rather than reset the file.
                var secondNight = importer.Import(BuildPlanJson(BuildTargetJson("M 16", 60.0, "OIII")));
                var secondRotator = new Mock<IRotatorMediator>();
                secondRotator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 75.0f });
                new NightFrontMetadataRecorder(secondNight, secondRotator.Object, "plan-night2.json", livePath);
                foreach (var exposure in FindTakeExposures(secondNight)) {
                    exposure.Status = SequenceEntityStatus.FINISHED;
                }

                var metadata = NightFrontMetadataStore.Load(livePath);
                Assert.Equal(2, metadata.CalibrationRequirements.Count);
                Assert.Contains(metadata.CalibrationRequirements, r => r.Filter == "Ha");
                Assert.Contains(metadata.CalibrationRequirements, r => r.Filter == "OIII");
            } finally {
                File.Delete(livePath);
            }
        }
    }
}
