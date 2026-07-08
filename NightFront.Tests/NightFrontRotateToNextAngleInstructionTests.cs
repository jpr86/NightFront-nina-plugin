using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using JeffRidder.NINA.Nightfront.Sequencer;
using Moq;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Settings.Default.NightFrontDataFolder is a process-wide static, so each test saves/restores it.
    // xUnit does not run [Fact]s within one class in parallel, so this is safe as long as no other
    // test class also mutates it.
    [Collection("NightFrontSettings")]
    public class NightFrontRotateToNextAngleInstructionTests {

        private static string CreateTempFolder() {
            var folder = Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public async System.Threading.Tasks.Task Execute_RotatesToHeadEntrysAngle_RoundedToNearestDegree() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, "TargetsForTonight");
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "Ha", 90.6, -1, -1);

                var rotatorMediator = new Mock<IRotatorMediator>();
                rotatorMediator.Setup(r => r.MoveMechanical(It.IsAny<float>(), It.IsAny<CancellationToken>())).ReturnsAsync(0f);

                var instruction = new NightFrontRotateToNextAngleInstruction(rotatorMediator.Object);

                await instruction.Execute(null, CancellationToken.None);

                rotatorMediator.Verify(r => r.MoveMechanical(91f, It.IsAny<CancellationToken>()), Times.Once);
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }

        [Fact]
        public void Validate_Fails_WhenNoCalibrationRequirementsRemain() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;

                var instruction = new NightFrontRotateToNextAngleInstruction(Mock.Of<IRotatorMediator>());

                var result = instruction.Validate();

                Assert.False(result);
                Assert.NotEmpty(instruction.Issues);
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }

        [Fact]
        public void Validate_Fails_WhenDataFolderNotConfigured() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            try {
                Settings.Default.NightFrontDataFolder = "";

                var instruction = new NightFrontRotateToNextAngleInstruction(Mock.Of<IRotatorMediator>());

                var result = instruction.Validate();

                Assert.False(result);
                Assert.NotEmpty(instruction.Issues);
            } finally {
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }
    }
}
