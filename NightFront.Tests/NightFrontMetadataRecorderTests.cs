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

        private static string BuildTargetJson(string name, double positionAngle, string filter) {
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
                  {{ ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": {{ ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""{filter}"" }} }}
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

        [Fact]
        public void Extract_DoesNotRecordAnythingUntilCenterAndRotateFinishes() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(BuildTargetJson("NGC 7000", 12.5, "Ha")));

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(new RotatorInfo { MechanicalPosition = 99f });

            var metadataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", metadataPath);

                Assert.False(File.Exists(metadataPath));
            } finally {
                File.Delete(metadataPath);
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

            var metadataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", metadataPath);
                var centerAndRotate = FindCenterAndRotate(imported);
                Assert.NotNull(centerAndRotate);
                centerAndRotate.Status = SequenceEntityStatus.FINISHED;

                var json = File.ReadAllText(metadataPath);
                var metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<NightFrontPlanMetadata>(json);
                Assert.NotNull(metadata);

                var requirement = Assert.Single(metadata.CalibrationRequirements);
                Assert.Equal("Ha", requirement.Filter);
                Assert.Equal(99.0, requirement.RotationAngle, 3);
                Assert.NotEqual(12.5, requirement.RotationAngle);
            } finally {
                File.Delete(metadataPath);
            }
        }

        [Fact]
        public void Extract_DedupesSameFilterWithinOneDegree_AcrossTargets() {
            var profileService = CreateProfileServiceWithFilters("Ha");
            var importer = CreateImporter(profileService);
            var imported = importer.Import(BuildPlanJson(
                BuildTargetJson("NGC 7000", 12.5, "Ha"),
                BuildTargetJson("M31", 45.0, "Ha")));

            var angles = new Queue<float>(new[] { 30.0f, 30.4f }); // within 1 degree of each other
            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(r => r.GetInfo()).Returns(() => new RotatorInfo { MechanicalPosition = angles.Dequeue() });

            var metadataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", metadataPath);

                var top = FindTopLevelDsoContainers(imported);
                foreach (var dso in top) {
                    var centerAndRotate = FindCenterAndRotate(new ISequenceItem[] { dso });
                    Assert.NotNull(centerAndRotate);
                    centerAndRotate.Status = SequenceEntityStatus.FINISHED;
                }

                var json = File.ReadAllText(metadataPath);
                var metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<NightFrontPlanMetadata>(json);
                Assert.NotNull(metadata);

                Assert.Single(metadata.CalibrationRequirements);
            } finally {
                File.Delete(metadataPath);
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

            var metadataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                new NightFrontMetadataRecorder(imported, rotatorMediator.Object, "plan.json", metadataPath);

                var top = FindTopLevelDsoContainers(imported);
                foreach (var dso in top) {
                    var centerAndRotate = FindCenterAndRotate(new ISequenceItem[] { dso });
                    Assert.NotNull(centerAndRotate);
                    centerAndRotate.Status = SequenceEntityStatus.FINISHED;
                }

                var json = File.ReadAllText(metadataPath);
                var metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<NightFrontPlanMetadata>(json);
                Assert.NotNull(metadata);

                Assert.Equal(2, metadata.CalibrationRequirements.Count);
            } finally {
                File.Delete(metadataPath);
            }
        }

        private static IEnumerable<ISequenceItem> FindTopLevelDsoContainers(IEnumerable<ISequenceItem> items) {
            return items.Where(i => i is DeepSkyObjectContainer);
        }
    }
}
