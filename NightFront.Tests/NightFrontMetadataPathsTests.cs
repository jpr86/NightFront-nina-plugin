using JeffRidder.NINA.Nightfront.Import;
using System;
using System.IO;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontMetadataPathsTests {

        [Theory]
        [InlineData("TargetsForTonight_2026-07-06", "2026-07-06", "TargetsForTonight")]
        [InlineData("TargetsForTonight-2026-07-06", "2026-07-06", "TargetsForTonight")]
        [InlineData("2026-07-06_TargetsForTonight", "2026-07-06", "TargetsForTonight")]
        [InlineData("2026-07-06", "2026-07-06", "NightFront")]
        [InlineData("SomePlan", "2026-07-06", "SomePlan")]
        public void DeriveStableBaseName_StripsTokenAndAdjacentSeparator(string planFileBaseName, string todayToken, string expected) {
            var result = NightFrontMetadataPaths.DeriveStableBaseName(planFileBaseName, todayToken);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ResolveBaseName_ExplicitNameWinsVerbatim_EvenIfFolderHasOthers() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "Other.metadata.json"), "{}");

                var result = NightFrontMetadataPaths.ResolveBaseName(folder, "ExplicitName", out var issue);

                Assert.Equal("ExplicitName", result);
                Assert.Null(issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveBaseName_ExactlyOneMetadataFile_ResolvesAutomatically() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight.metadata.json"), "{}");

                var result = NightFrontMetadataPaths.ResolveBaseName(folder, "", out var issue);

                Assert.Equal("TargetsForTonight", result);
                Assert.Null(issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveBaseName_NoMetadataFiles_ReturnsNullWithIssue() {
            var folder = CreateTempFolder();
            try {
                var result = NightFrontMetadataPaths.ResolveBaseName(folder, "", out var issue);

                Assert.Null(result);
                Assert.NotNull(issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveBaseName_MultipleMetadataFiles_ReturnsNullWithIssueNamingCandidates() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight.metadata.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "WinterProject.metadata.json"), "{}");

                var result = NightFrontMetadataPaths.ResolveBaseName(folder, "", out var issue);

                Assert.Null(result);
                Assert.Contains("TargetsForTonight", issue);
                Assert.Contains("WinterProject", issue);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void ResolveBaseName_IgnoresTheSharedArchiveFile() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight.metadata.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "archived.metadata.json"), "{}");

                var result = NightFrontMetadataPaths.ResolveBaseName(folder, "", out var issue);

                Assert.Equal("TargetsForTonight", result);
                Assert.Null(issue);
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

        // ── FindMostRecentPlanFile ───────────────────────────────────────────────────────────

        [Fact]
        public void FindMostRecentPlanFile_ReturnsThePlanFile_EvenWhenItsDateDoesNotMatchToday() {
            // The whole point of this method: a replan running after local midnight has a "today"
            // that no longer matches the date baked into the still-in-progress night's plan
            // filename - FindTodaysPlanFile alone would find nothing here.
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-14.json"), "{}");

                var result = NightFrontMetadataPaths.FindMostRecentPlanFile(folder);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-14.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindMostRecentPlanFile_ExcludesAllTheSameSidecarsAsFindTodaysPlanFile() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-14.progress.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-14.metadata.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "selection.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "session-config.json"), "{}");
                File.WriteAllText(Path.Combine(folder, "TargetsForTonight_2026-07-14.json"), "{}");

                var result = NightFrontMetadataPaths.FindMostRecentPlanFile(folder);

                Assert.Equal(Path.Combine(folder, "TargetsForTonight_2026-07-14.json"), result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindMostRecentPlanFile_MultipleCandidates_ReturnsTheMostRecentlyWritten() {
            var folder = CreateTempFolder();
            try {
                var older = Path.Combine(folder, "TargetsForTonight_2026-07-13.json");
                var newer = Path.Combine(folder, "TargetsForTonight_2026-07-14.json");
                File.WriteAllText(older, "{}");
                File.SetLastWriteTimeUtc(older, new DateTime(2026, 7, 13, 20, 0, 0, DateTimeKind.Utc));
                File.WriteAllText(newer, "{}");
                File.SetLastWriteTimeUtc(newer, new DateTime(2026, 7, 14, 20, 0, 0, DateTimeKind.Utc));

                var result = NightFrontMetadataPaths.FindMostRecentPlanFile(folder);

                Assert.Equal(newer, result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindMostRecentPlanFile_OnlySidecarsPresent_ReturnsNull() {
            var folder = CreateTempFolder();
            try {
                File.WriteAllText(Path.Combine(folder, "selection.json"), "{}");

                var result = NightFrontMetadataPaths.FindMostRecentPlanFile(folder);

                Assert.Null(result);
            } finally {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FindMostRecentPlanFile_FolderDoesNotExist_ReturnsNull() {
            var result = NightFrontMetadataPaths.FindMostRecentPlanFile(Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid()));

            Assert.Null(result);
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
