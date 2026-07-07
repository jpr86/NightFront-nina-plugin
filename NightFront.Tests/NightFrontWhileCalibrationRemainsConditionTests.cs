using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using JeffRidder.NINA.Nightfront.Sequencer;
using System;
using System.IO;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Settings.Default.NightFrontDataFolder is a process-wide static, so each test saves/restores it.
    [Collection("NightFrontSettings")]
    public class NightFrontWhileCalibrationRemainsConditionTests {

        private static string CreateTempFolder() {
            var folder = Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Fact]
        public void Check_TrueWhileEntriesRemain_FalseOnceEmpty() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, "TargetsForTonight");
                var archivedPath = NightFrontMetadataPaths.GetArchivedMetadataPath(folder);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, archivedPath, "Ha", 90.0, -1, -1);

                var condition = new NightFrontWhileCalibrationRemainsCondition();

                Assert.True(condition.Check(null, null));

                NightFrontMetadataStore.ArchiveClaimed(archivedPath, NightFrontMetadataStore.ClaimNext(livePath));

                Assert.False(condition.Check(null, null));
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }

        [Fact]
        public void Check_ReturnsFalse_WhenDataFolderNotConfigured() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            try {
                Settings.Default.NightFrontDataFolder = "";
                var condition = new NightFrontWhileCalibrationRemainsCondition();

                Assert.False(condition.Check(null, null));
            } finally {
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }
    }
}
