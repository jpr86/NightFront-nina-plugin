using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Astrometry.Interfaces;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Full Execute()-level coverage (rotate + shoot flats via NINA's real SkyFlat internals, driven
    // by Settings.Default.NightFrontDataFolder) is intentionally not exercised here - it depends on
    // NINA's live camera/imaging pipeline and needs manual verification against a real NINA install
    // (see the implementation plan). These tests cover what's safely testable against mocks: that
    // construction wires up the persistent rotate+flat children, and that Clone() preserves the
    // user-configured properties.
    public class NightFrontSkyFlatsInstructionTests {

        private static NightFrontSkyFlatsInstruction CreateInstruction() {
            // SkyFlat's own children (SwitchFilter, TakeExposure, ...) and this base class's own
            // MoveRotatorMechanical child all validate themselves as soon as they're Add()-ed to
            // their parent (NINA's AttachNewParent -> AfterParentChanged -> Validate() chain) -
            // TakeExposure.Validate() dereferences the camera mediator's GetInfo() result and
            // profileService.ActiveProfile.ImageFileSettings unconditionally, and
            // MoveRotatorMechanical.Validate() dereferences the rotator mediator's GetInfo() result -
            // so those need real (non-null) instances rather than Moq's default loose-mock nulls.
            var cameraMediator = new Mock<ICameraMediator>();
            cameraMediator.Setup(m => m.GetInfo()).Returns(new CameraInfo());

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(m => m.GetInfo()).Returns(new RotatorInfo());

            var imageFileSettings = new Mock<IImageFileSettings>();
            imageFileSettings.SetupGet(s => s.FilePath).Returns(System.IO.Path.GetTempPath());

            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.ImageFileSettings).Returns(imageFileSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(p => p.ActiveProfile).Returns(profile.Object);

            return new NightFrontSkyFlatsInstruction(
                profileService.Object,
                cameraMediator.Object,
                Mock.Of<ITelescopeMediator>(),
                Mock.Of<IImagingMediator>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IImageHistoryVM>(),
                Mock.Of<IFilterWheelMediator>(),
                rotatorMediator.Object,
                Mock.Of<ITwilightCalculator>());
        }

        [Fact]
        public void Constructor_BuildsPersistentRotateAndFlatChildren() {
            var instruction = CreateInstruction();

            Assert.Equal(2, instruction.Items.Count);
        }

        [Fact]
        public void Clone_PreservesUserConfiguredProperties() {
            var instruction = CreateInstruction();
            instruction.MinExposure = 1;
            instruction.MaxExposure = 5;
            instruction.HistogramTargetPercentage = 0.5;
            instruction.HistogramTolerancePercentage = 0.2;
            instruction.ShouldDither = true;
            instruction.Amount = 15;

            var clone = (NightFrontSkyFlatsInstruction)instruction.Clone();

            Assert.NotSame(instruction, clone);
            Assert.Equal(1, clone.MinExposure);
            Assert.Equal(5, clone.MaxExposure);
            Assert.Equal(0.5, clone.HistogramTargetPercentage);
            Assert.Equal(0.2, clone.HistogramTolerancePercentage);
            Assert.True(clone.ShouldDither);
            Assert.Equal(15, clone.Amount);
            Assert.Equal(2, clone.Items.Count);
        }
    }
}
