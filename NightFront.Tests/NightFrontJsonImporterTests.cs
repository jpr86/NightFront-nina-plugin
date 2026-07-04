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
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
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
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Note: tests that populate InputCoordinates on a real InputTarget (the full-graph happy path)
    // transitively call into NINA.Astrometry's NOVAS31lib.dll native P/Invoke library for
    // horizon/transit calculations. That DLL ships only with the actual NINA installer, not the
    // NINA.Astrometry NuGet package, so those tests require running on a machine with NINA
    // installed (e.g. by copying NOVAS31lib.dll from the NINA install directory into this test
    // project's output folder). The remaining tests exercise the importer's JSON-walking, type
    // dispatch, and error-handling logic without touching astrometry code and run anywhere.
    public class NightFrontJsonImporterTests {
        private const string SamplePlanJson = @"{
  ""$id"": ""1"",
  ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
  ""Name"": ""TargetsForTonight"",
  ""Items"": {
    ""$id"": ""2"",
    ""$type"": ""System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel"",
    ""$values"": [
      {
        ""$id"": ""3"",
        ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"",
        ""Name"": ""NGC 7000"",
        ""Target"": {
          ""$id"": ""4"",
          ""$type"": ""NINA.Astrometry.InputTarget, NINA.Astrometry"",
          ""TargetName"": ""NGC 7000"",
          ""PositionAngle"": 12.5,
          ""InputCoordinates"": {
            ""$id"": ""5"",
            ""$type"": ""NINA.Astrometry.InputCoordinates, NINA.Astrometry"",
            ""RAHours"": 20,
            ""RAMinutes"": 59,
            ""RASeconds"": 17.1,
            ""NegativeDec"": false,
            ""DecDegrees"": 44,
            ""DecMinutes"": 31,
            ""DecSeconds"": 44.0
          }
        },
        ""Items"": {
          ""$id"": ""6"",
          ""$type"": ""System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel"",
          ""$values"": [
            { ""$type"": ""NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate, NINA.Sequencer"", ""PositionAngle"": 12.5 },
            { ""$type"": ""NINA.Sequencer.SequenceItem.Autofocus.RunAutofocus, NINA.Sequencer"" },
            { ""$type"": ""NINA.Sequencer.SequenceItem.Guider.StartGuiding, NINA.Sequencer"", ""ForceCalibration"": false },
            {
              ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
              ""Name"": ""Targets_For_Tonight"",
              ""Conditions"": {
                ""$values"": [
                  { ""$type"": ""NINA.Sequencer.Conditions.TimeCondition, NINA.Sequencer"", ""Hours"": 2, ""Minutes"": 15, ""Seconds"": 0, ""MinutesOffset"": 0 }
                ]
              },
              ""Items"": {
                ""$values"": [
                  {
                    ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"",
                    ""Name"": ""12x300s"",
                    ""Conditions"": {
                      ""$values"": [
                        { ""$type"": ""NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer"", ""Iterations"": 12 }
                      ]
                    },
                    ""Items"": {
                      ""$values"": [
                        {
                          ""$type"": ""NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter, NINA.Sequencer"",
                          ""Filter"": { ""$type"": ""NINA.Core.Model.Equipment.FilterInfo, NINA.Core"", ""_name"": ""Ha"", ""_focusOffset"": 0, ""_position"": 0 }
                        },
                        {
                          ""$type"": ""NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer"",
                          ""ExposureTime"": 300.0,
                          ""Gain"": 100,
                          ""Offset"": 10,
                          ""Binning"": { ""$type"": ""NINA.Core.Model.Equipment.BinningMode, NINA.Core"", ""X"": 1, ""Y"": 1 },
                          ""ImageType"": ""LIGHT"",
                          ""ExposureCount"": 0
                        }
                      ]
                    }
                  },
                  { ""$type"": ""NINA.Sequencer.SequenceItem.Guider.Dither, NINA.Sequencer"" }
                ]
              }
            }
          ]
        }
      }
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

        private static IProfileService CreateProfileServiceWithFilter(string filterName) {
            var filter = new FilterInfo(filterName, 0, 0);

            var astrometrySettings = new Mock<IAstrometrySettings>();
            astrometrySettings.SetupGet(x => x.Latitude).Returns(45.0);
            astrometrySettings.SetupGet(x => x.Longitude).Returns(-93.0);
            astrometrySettings.SetupGet(x => x.Horizon).Returns((CustomHorizon)null!);

            var filterWheelSettings = new Mock<IFilterWheelSettings>();
            filterWheelSettings.SetupGet(x => x.FilterWheelFilters).Returns(new ObserveAllCollection<FilterInfo>(new[] { filter }));

            var profile = new Mock<IProfile>();
            profile.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettings.Object);
            profile.SetupGet(x => x.FilterWheelSettings).Returns(filterWheelSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(x => x.ActiveProfile).Returns(profile.Object);

            return profileService.Object;
        }

        [Fact]
        public void Import_ParsesFullSamplePlan_IntoExpectedObjectGraph() {
            var profileService = CreateProfileServiceWithFilter("Ha");
            var importer = CreateImporter(profileService);

            var result = importer.Import(SamplePlanJson);

            var target = Assert.Single(result);
            var dsoContainer = Assert.IsType<DeepSkyObjectContainer>(target);
            Assert.Equal("NGC 7000", dsoContainer.Name);
            Assert.Equal("NGC 7000", dsoContainer.Target.TargetName);
            Assert.Equal(20, dsoContainer.Target.InputCoordinates.RAHours);
            Assert.Equal(44, dsoContainer.Target.InputCoordinates.DecDegrees);

            Assert.Equal(4, dsoContainer.Items.Count);
            Assert.IsType<CenterAndRotate>(dsoContainer.Items[0]);
            Assert.IsType<RunAutofocus>(dsoContainer.Items[1]);
            Assert.IsType<StartGuiding>(dsoContainer.Items[2]);

            var nightlyLoop = Assert.IsType<SequentialContainer>(dsoContainer.Items[3]);
            Assert.Equal("Targets_For_Tonight", nightlyLoop.Name);
            var timeCondition = Assert.IsType<TimeCondition>(Assert.Single(nightlyLoop.Conditions));
            Assert.Equal(2, timeCondition.Hours);
            Assert.Equal(15, timeCondition.Minutes);

            Assert.Equal(2, nightlyLoop.Items.Count);
            var exposureBlock = Assert.IsType<SequentialContainer>(nightlyLoop.Items[0]);
            Assert.Equal("12x300s", exposureBlock.Name);
            var loopCondition = Assert.IsType<LoopCondition>(Assert.Single(exposureBlock.Conditions));
            Assert.Equal(12, loopCondition.Iterations);

            Assert.Equal(2, exposureBlock.Items.Count);
            var switchFilter = Assert.IsType<SwitchFilter>(exposureBlock.Items[0]);
            Assert.Equal("Ha", switchFilter.Filter.Name);
            var takeExposure = Assert.IsType<TakeExposure>(exposureBlock.Items[1]);
            Assert.Equal(300.0, takeExposure.ExposureTime);
            Assert.Equal(100, takeExposure.Gain);
            Assert.Equal(10, takeExposure.Offset);
            Assert.Equal(1, takeExposure.Binning.X);
            Assert.Equal(1, takeExposure.Binning.Y);

            Assert.IsType<Dither>(nightlyLoop.Items[1]);
        }

        [Fact]
        public void Import_UnknownFilter_ThrowsNightFrontImportException() {
            var profileService = CreateProfileServiceWithFilter("OIII");
            var importer = CreateImporter(profileService);

            var ex = Assert.Throws<NightFrontImportException>(() => importer.Import(SamplePlanJson));
            Assert.Contains("Ha", ex.Message);
        }

        [Fact]
        public void Import_UnsupportedType_ThrowsNightFrontImportException() {
            var profileService = CreateProfileServiceWithFilter("Ha");
            var importer = CreateImporter(profileService);
            var json = @"{ ""$type"": ""NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer"", ""Items"": { ""$values"": [ { ""$type"": ""NINA.Sequencer.SequenceItem.SomeFuture.NewInstruction, NINA.Sequencer"" } ] } }";

            var ex = Assert.Throws<NightFrontImportException>(() => importer.Import(json));
            Assert.Contains("NewInstruction", ex.Message);
        }

        [Fact]
        public void Import_MalformedJson_ThrowsNightFrontImportException() {
            var profileService = CreateProfileServiceWithFilter("Ha");
            var importer = CreateImporter(profileService);

            Assert.Throws<NightFrontImportException>(() => importer.Import("{ not valid json"));
        }

        [Fact]
        public void Import_WrongRootType_ThrowsNightFrontImportException() {
            var profileService = CreateProfileServiceWithFilter("Ha");
            var importer = CreateImporter(profileService);
            var json = @"{ ""$type"": ""NINA.Sequencer.Container.DeepSkyObjectContainer, NINA.Sequencer"" }";

            Assert.Throws<NightFrontImportException>(() => importer.Import(json));
        }
    }
}
