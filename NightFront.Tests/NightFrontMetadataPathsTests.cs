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

        private static string CreateTempFolder() {
            var folder = Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
