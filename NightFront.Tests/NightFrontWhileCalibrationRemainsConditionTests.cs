using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using JeffRidder.NINA.Nightfront.Sequencer;
using System;
using System.Collections.Generic;
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
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "Ha", 90.0, -1, -1);

                var condition = new NightFrontWhileCalibrationRemainsCondition();

                Assert.True(condition.Check(null, null));

                var claimed = NightFrontMetadataStore.ClaimNext(livePath, new List<string>());
                NightFrontMetadataStore.MarkCompleted(livePath, claimed.Id);

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
