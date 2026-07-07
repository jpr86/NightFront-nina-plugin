using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFlatDevice;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // See NightFrontSkyFlatsInstructionTests for why full Execute()-level coverage is out of scope
    // here (needs a live NINA host); these cover construction and Clone() against mocks.
    public class NightFrontTrainedFlatsInstructionTests {

        private static NightFrontTrainedFlatsInstruction CreateInstruction() {
            // TrainedFlatExposure's own children (CloseCover, TakeExposure, ...) and this base
            // class's own MoveRotatorMechanical child all validate themselves as soon as they're
            // Add()-ed to their parent - CloseCover.Validate() dereferences the flat device
            // mediator's GetInfo() result, TakeExposure.Validate() dereferences the camera
            // mediator's GetInfo() result and profileService.ActiveProfile.ImageFileSettings, and
            // MoveRotatorMechanical.Validate() dereferences the rotator mediator's GetInfo() result -
            // so those need real (non-null) instances rather than Moq's default loose-mock nulls.
            var cameraMediator = new Mock<ICameraMediator>();
            cameraMediator.Setup(m => m.GetInfo()).Returns(new CameraInfo());

            var flatDeviceMediator = new Mock<IFlatDeviceMediator>();
            flatDeviceMediator.Setup(m => m.GetInfo()).Returns(new FlatDeviceInfo());

            var rotatorMediator = new Mock<IRotatorMediator>();
            rotatorMediator.Setup(m => m.GetInfo()).Returns(new RotatorInfo());

            var imageFileSettings = new Mock<IImageFileSettings>();
            imageFileSettings.SetupGet(s => s.FilePath).Returns(System.IO.Path.GetTempPath());

            var profile = new Mock<IProfile>();
            profile.SetupGet(p => p.ImageFileSettings).Returns(imageFileSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(p => p.ActiveProfile).Returns(profile.Object);

            return new NightFrontTrainedFlatsInstruction(
                profileService.Object,
                cameraMediator.Object,
                Mock.Of<IImagingMediator>(),
                Mock.Of<IImageSaveMediator>(),
                Mock.Of<IImageHistoryVM>(),
                Mock.Of<IFilterWheelMediator>(),
                rotatorMediator.Object,
                flatDeviceMediator.Object);
        }

        [Fact]
        public void Constructor_BuildsPersistentRotateAndFlatChildren() {
            var instruction = CreateInstruction();

            Assert.Equal(2, instruction.Items.Count);
        }

        [Fact]
        public void Clone_PreservesUserConfiguredProperties() {
            var instruction = CreateInstruction();
            instruction.BaseName = "TargetsForTonight";
            instruction.Amount = 15;

            var clone = (NightFrontTrainedFlatsInstruction)instruction.Clone();

            Assert.NotSame(instruction, clone);
            Assert.Equal("TargetsForTonight", clone.BaseName);
            Assert.Equal(15, clone.Amount);
            Assert.Equal(2, clone.Items.Count);
        }
    }
}
