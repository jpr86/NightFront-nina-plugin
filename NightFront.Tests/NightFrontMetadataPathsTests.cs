using JeffRidder.NINA.Nightfront.Import;
using System;
using System.IO;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontMetadataPathsTests {

        [Fact]
        public void GetLiveMetadataPath_IsAlwaysTheSameFixedUndatedFileName() {
            var result = NightFrontMetadataPaths.GetLiveMetadataPath("C:\\NightFrontData");

            Assert.Equal(Path.Combine("C:\\NightFrontData", "calibration.metadata.json"), result);
        }

        [Fact]
        public void ResolveExistingMetadataPath_FileExists_ReturnsItWithNoIssue() {
            var folder = CreateTempFolder();
            try {
                var path = Path.Combine(folder, "calibration.metadata.json");
                File.WriteAllText(path, "{}");

                var result = NightFrontMetadataPaths.ResolveExistingMetadataPath(folder, out var issue);

                Assert.Equal(path, result);
                Assert.Null(issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveExistingMetadataPath_FileAbsent_ReturnsNullWithIssue() {
            var folder = CreateTempFolder();
            try {
                var result = NightFrontMetadataPaths.ResolveExistingMetadataPath(folder, out var issue);

                Assert.Null(result);
                Assert.NotNull(issue);
                Assert.Contains("calibration.metadata.json", issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveExistingMetadataPath_FolderMissing_ReturnsNullWithIssue() {
            var result = NightFrontMetadataPaths.ResolveExistingMetadataPath(
                Path.Combine(Path.GetTempPath(), "NightFrontTests_DoesNotExist_" + Guid.NewGuid()), out var issue);

            Assert.Null(result);
            Assert.NotNull(issue);
        }

        [Fact]
        public void FindTodaysPlanFile_ExcludesTheFixedCalibrationMetadataFile() {
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                File.WriteAllText(Path.Combine(folder, "calibration.metadata.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindTodaysPlanFile_ExcludesProgressSnapshotFile_EvenWhenDateStampedLikeThePlan() {
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                // A progress snapshot named the same way NightFrontProgressSnapshotWriter's own
                // caller is expected to name one - date-stamped like the plan file it was captured
                // from (see GetProgressSnapshotPath) - must not be mistaken for the real plan file.
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.progress.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindTodaysPlanFile_OnlyAProgressSnapshotPresent_ReturnsNull() {
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.progress.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Null(result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void GetProgressSnapshotPath_CombinesFolderBaseNameAndSuffix() {
            var result = NightFrontMetadataPaths.GetProgressSnapshotPath("C:\\NightFrontData", "TargetsForTonight_2026-07-06");

            Assert.Equal(Path.Combine("C:\\NightFrontData", "TargetsForTonight_2026-07-06.progress.json"), result);
        }

        [Fact]
        public void GetSelectionPreferencePath_IsAFixedFileNameNotBaseNameKeyed() {
            var result = NightFrontMetadataPaths.GetSelectionPreferencePath("C:\\NightFrontData");

            Assert.Equal(Path.Combine("C:\\NightFrontData", "selection.json"), result);
        }

        [Fact]
        public void GetSessionConfigPath_IsAFixedFileNameNotBaseNameKeyed() {
            var result = NightFrontMetadataPaths.GetSessionConfigPath("C:\\NightFrontData");

            Assert.Equal(Path.Combine("C:\\NightFrontData", "session-config.json"), result);
        }

        [Fact]
        public void FindTodaysPlanFile_ExcludesTheSelectionPreferenceAndSessionConfigSidecars() {
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                File.WriteAllText(Path.Combine(folder, "selection.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "session-config.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindTodaysPlanFile_OnlySidecarsPresent_ReturnsNull() {
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                File.WriteAllText(Path.Combine(folder, "selection.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "session-config.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Null(result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void GetReplanHistoryFolder_IsASubfolderOfTheDataFolder() {
            var result = NightFrontMetadataPaths.GetReplanHistoryFolder("C:\\NightFrontData");

            Assert.Equal(Path.Combine("C:\\NightFrontData", "replan-history"), result);
        }

        [Fact]
        public void GetReplanHistoryPath_IsTimestampedSoSameNightReplansDoNotCollide() {
            var first = NightFrontMetadataPaths.GetReplanHistoryPath(
                "C:\\NightFrontData", "TargetsForTonight_2026-07-06.json", new DateTime(2026, 7, 6, 22, 15, 0));
            var second = NightFrontMetadataPaths.GetReplanHistoryPath(
                "C:\\NightFrontData", "TargetsForTonight_2026-07-06.json", new DateTime(2026, 7, 7, 1, 30, 0));

            Assert.NotEqual(first, second);
            Assert.Equal(Path.Combine("C:\\NightFrontData", "replan-history", "20260706-221500_TargetsForTonight_2026-07-06.json"), first);
            Assert.Equal(Path.Combine("C:\\NightFrontData", "replan-history", "20260707-013000_TargetsForTonight_2026-07-06.json"), second);
        }

        [Fact]
        public void FindTodaysPlanFile_DoesNotDescendIntoTheReplanHistorySubfolder() {
            // A dated plan-file copy sitting inside replan-history (an archived pre-replan snapshot -
            // see NightFrontReplanInstruction.ArchivePreviousPlanFileIfPresent) must never be mistaken
            // for today's real plan file, the same way the other sidecar exclusions work - but here
            // it's guaranteed for free by Directory.EnumerateFiles' default non-recursive search,
            // rather than needing its own explicit exclusion clause.
            var folder = CreateTempFolder();
            try {
                var today = new DateTime(2026, 7, 6);
                var historyFolder = NightFrontMetadataPaths.GetReplanHistoryFolder(folder);
                Directory.CreateDirectory(historyFolder);
                File.WriteAllText(Path.Combine(historyFolder, "20260706-201500_TargetsForTonight_2026-07-06.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), "{}");

                var result = NightFrontMetadataPaths.FindTodaysPlanFile(folder, today);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-06.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        // ── ResolveCliPath / CoLocatedCliPath ─────────────────────────────────────────────────

        [Fact]
        public void ResolveCliPath_NoOverride_ResolvesTheCoLocatedExeWhenPresent() {
            var assemblyDir = CreateTempFolder();
            File.WriteAllText(Path.Combine(assemblyDir, "nightfront-cli.exe"), "stub");
            try {
                var result = NightFrontMetadataPaths.ResolveCliPath(
                    overridePath: null, assemblyLocation: Path.Combine(assemblyDir, "JeffRidder.NINA.Nightfront.dll"));

                Assert.Equal(Path.Combine(assemblyDir, "nightfront-cli.exe"), result);
            } finally {
                Directory.Delete(assemblyDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveCliPath_NoOverrideAndNoCoLocatedExe_ReturnsNull() {
            var assemblyDir = CreateTempFolder();
            try {
                var result = NightFrontMetadataPaths.ResolveCliPath(
                    overridePath: "", assemblyLocation: Path.Combine(assemblyDir, "JeffRidder.NINA.Nightfront.dll"));

                Assert.Null(result);
            } finally {
                Directory.Delete(assemblyDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveCliPath_ExistingOverride_TakesPrecedenceOverTheCoLocatedExe() {
            var assemblyDir = CreateTempFolder();
            File.WriteAllText(Path.Combine(assemblyDir, "nightfront-cli.exe"), "stub");
            var overridePath = Path.Combine(CreateTempFolder(), "override.exe");
            File.WriteAllText(overridePath, "stub");
            try {
                var result = NightFrontMetadataPaths.ResolveCliPath(
                    overridePath, assemblyLocation: Path.Combine(assemblyDir, "JeffRidder.NINA.Nightfront.dll"));

                Assert.Equal(overridePath, result);
            } finally {
                Directory.Delete(assemblyDir, recursive: true);
                Directory.Delete(Path.GetDirectoryName(overridePath), recursive: true);
            }
        }

        [Fact]
        public void ResolveCliPath_OverrideSetButMissing_FallsBackToTheCoLocatedExe() {
            // A stale/hand-typo'd NightFrontCliPath shouldn't silently break replan if a good
            // co-located build exists - only an override that actually resolves wins.
            var assemblyDir = CreateTempFolder();
            File.WriteAllText(Path.Combine(assemblyDir, "nightfront-cli.exe"), "stub");
            try {
                var result = NightFrontMetadataPaths.ResolveCliPath(
                    overridePath: @"C:\this\path\does\not\exist.exe",
                    assemblyLocation: Path.Combine(assemblyDir, "JeffRidder.NINA.Nightfront.dll"));

                Assert.Equal(Path.Combine(assemblyDir, "nightfront-cli.exe"), result);
            } finally {
                Directory.Delete(assemblyDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveCliPath_NeitherOverrideNorCoLocatedExeExist_ReturnsNull() {
            var assemblyDir = CreateTempFolder();
            try {
                var result = NightFrontMetadataPaths.ResolveCliPath(
                    overridePath: @"C:\this\path\does\not\exist.exe",
                    assemblyLocation: Path.Combine(assemblyDir, "JeffRidder.NINA.Nightfront.dll"));

                Assert.Null(result);
            } finally {
                Directory.Delete(assemblyDir, recursive: true);
            }
        }

        [Fact]
        public void CoLocatedCliPath_IsAlwaysComputedRegardlessOfWhetherTheFileExists() {
            var result = NightFrontMetadataPaths.CoLocatedCliPath(@"C:\Plugins\JeffRidder.NINA.Nightfront\JeffRidder.NINA.Nightfront.dll");

            Assert.Equal(@"C:\Plugins\JeffRidder.NINA.Nightfront\nightfront-cli.exe", result);
        }

        private static string CreateTempFolder() {
            var folder = Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
