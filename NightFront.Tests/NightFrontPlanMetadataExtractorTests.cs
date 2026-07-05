using JeffRidder.NINA.Nightfront.Import;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.Core.Utility.WindowService;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Builds test object graphs by running real plan JSON through NightFrontJsonImporter (the same
    // approach as NightFrontJsonImporterTests) rather than hand-constructing NINA.Sequencer objects
    // directly, since several of them (DeepSkyObjectContainer, CenterAndRotate, SwitchFilter)
    // require multiple mediator/factory constructor arguments the importer already knows how to
    // supply. Like NightFrontJsonImporterTests' full-graph tests, these populate InputCoordinates on
    // a real InputTarget - required for CenterAndRotate to attach to its parent without throwing -
    // which transitively calls into NOVAS31lib.dll (see NightFrontJsonImporterTests' class comment),
    // so these tests also require that DLL to be present in the test output folder.
    public class NightFrontPlanMetadataExtractorTests {
        private const string TwoTargetsPlusBareFilterPlanJson = @"{
  ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
  ""Name"": ""TargetsForTonight"",
  ""Items"": {
    ""$values"": [
      {
        ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
        ""Name"": ""NGC 7000"",
        ""Target"": {
          ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
          ""TargetName"": ""NGC 7000"",
          ""PositionAngle"": 12.5,
          ""InputCoordinates"": {
            ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
            ""RAHours"": 20, ""RAMinutes"": 59, ""RASeconds"": 17.1,
            ""NegativeDec"": false, ""DecDegrees"": 44, ""DecMinutes"": 31, ""DecSeconds"": 44.0
          }
        },
        ""Items"": {
          ""$values"": [
            { ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": 12.5 },
            {
              ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
              ""Name"": ""12x300s"",
              ""Conditions"": { ""$values"": [ { ""$type"": ""NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer"", ""Iterations"": 12 } ] },
              ""Items"": {
                ""$values"": [
                  { ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": { ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""Ha"" } },
                  { ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""ExposureTime"": 300.0 }
                ]
              }
            },
            { ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": { ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""OIII"" } },
            { ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""ExposureTime"": 300.0 }
          ]
        }
      },
      {
        ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
        ""Name"": ""M31"",
        ""Target"": {
          ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
          ""TargetName"": ""M31"",
          ""PositionAngle"": 45.0,
          ""InputCoordinates"": {
            ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
            ""RAHours"": 0, ""RAMinutes"": 42, ""RASeconds"": 44.3,
            ""NegativeDec"": false, ""DecDegrees"": 41, ""DecMinutes"": 16, ""DecSeconds"": 9.0
          }
        },
        ""Items"": {
          ""$values"": [
            { ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": 90.0 },
            { ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": { ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""Ha"" } },
            { ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"", ""ExposureTime"": 300.0 }
          ]
        }
      },
      { ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"", ""Filter"": { ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""L"" } }
    ]
  }
}";

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

        private static IList<ISequenceItem> ImportSamplePlan() {
            var profileService = CreateProfileServiceWithFilters("Ha", "OIII", "L");
            var importer = CreateImporter(profileService);
            return importer.Import(TwoTargetsPlusBareFilterPlanJson);
        }

        [Fact]
        public void Extract_GroupsFiltersAndAnglesPerTarget() {
            var imported = ImportSamplePlan();

            var metadata = NightFrontPlanMetadataExtractor.Extract(imported, "2026-07-05-plan.json");

            var ngc7000 = Assert.Single(metadata.Targets, t => t.TargetName == "NGC 7000");
            Assert.Equal(new[] { 12.5 }, ngc7000.RotationAngles);
            Assert.Equal(new[] { "Ha", "OIII" }, ngc7000.Filters.OrderBy(f => f));

            var m31 = Assert.Single(metadata.Targets, t => t.TargetName == "M31");
            Assert.Equal(new[] { 45.0, 90.0 }, m31.RotationAngles.OrderBy(a => a));
            Assert.Equal(new[] { "Ha" }, m31.Filters);
        }

        [Fact]
        public void Extract_BareTopLevelItemWithNoEnclosingTarget_GoesToUngroupedBucket() {
            var imported = ImportSamplePlan();

            var metadata = NightFrontPlanMetadataExtractor.Extract(imported, "2026-07-05-plan.json");

            var ungrouped = Assert.Single(metadata.Targets, t => t.TargetName == "Ungrouped");
            Assert.Equal(new[] { "L" }, ungrouped.Filters);
        }

        [Fact]
        public void Extract_RollsUpDistinctFiltersAndAnglesAcrossTargets() {
            var imported = ImportSamplePlan();

            var metadata = NightFrontPlanMetadataExtractor.Extract(imported, "2026-07-05-plan.json");

            // "Ha" is used by both NGC 7000 and M31 but should only appear once at the top level.
            Assert.Equal(new[] { "Ha", "L", "OIII" }, metadata.Filters.OrderBy(f => f));
            Assert.Equal(new[] { 12.5, 45.0, 90.0 }, metadata.RotationAngles.OrderBy(a => a));
        }

        [Fact]
        public void Extract_SetsSourcePlanFileAndDate() {
            var imported = ImportSamplePlan();

            var metadata = NightFrontPlanMetadataExtractor.Extract(imported, "2026-07-05-plan.json");

            Assert.Equal("2026-07-05-plan.json", metadata.SourcePlanFile);
            Assert.False(string.IsNullOrWhiteSpace(metadata.Date));
        }
    }
}
