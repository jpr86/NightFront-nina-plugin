using JeffRidder.NINA.Nightfront.Import;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontProgressSnapshotWriterTests {

        private static string CreateTempPath() {
            return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".progress.json");
        }

        [Fact]
        public void Write_ThenReadBack_RoundTripsContent() {
            var path = CreateTempPath();
            try {
                var snapshot = new NightFrontProgressSnapshot {
                    GeneratedAtUtc = new DateTime(2026, 7, 9, 3, 30, 0, DateTimeKind.Utc),
                    Targets = {
                        new NightFrontTargetProgress { Name = "M 63", PlannedCount = 12, CompletedCount = 5, Status = "RUNNING" },
                        new NightFrontTargetProgress { Name = "LDN 43", PlannedCount = 10, CompletedCount = 0, Status = "CREATED" },
                    }
                };

                var succeeded = NightFrontProgressSnapshotWriter.Write(path, snapshot);

                Assert.True(succeeded);
                var roundTripped = JsonConvert.DeserializeObject<NightFrontProgressSnapshot>(File.ReadAllText(path));
                Assert.NotNull(roundTripped);
                Assert.Equal(snapshot.GeneratedAtUtc, roundTripped.GeneratedAtUtc);
                Assert.Equal(2, roundTripped.Targets.Count);
                Assert.Equal("M 63", roundTripped.Targets[0].Name);
                Assert.Equal(12, roundTripped.Targets[0].PlannedCount);
                Assert.Equal(5, roundTripped.Targets[0].CompletedCount);
                Assert.Equal("RUNNING", roundTripped.Targets[0].Status);
                Assert.Equal("LDN 43", roundTripped.Targets[1].Name);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Write_ExistingFile_IsOverwrittenNotAppended() {
            var path = CreateTempPath();
            try {
                File.WriteAllText(path, "stale contents that should be fully replaced, not appended to");

                var succeeded = NightFrontProgressSnapshotWriter.Write(path, new NightFrontProgressSnapshot());

                Assert.True(succeeded);
                var roundTripped = JsonConvert.DeserializeObject<NightFrontProgressSnapshot>(File.ReadAllText(path));
                Assert.NotNull(roundTripped);
                Assert.Empty(roundTripped.Targets);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Write_NoTempFileLeftBehindOnSuccess() {
            var path = CreateTempPath();
            try {
                NightFrontProgressSnapshotWriter.Write(path, new NightFrontProgressSnapshot());

                Assert.Empty(SiblingTempFiles(path));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Write_UnwritableDirectory_ReturnsFalseAndDoesNotThrow() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent-dir", "snapshot.json");

            var succeeded = NightFrontProgressSnapshotWriter.Write(path, new NightFrontProgressSnapshot());

            Assert.False(succeeded);
        }

        [Fact]
        public async Task Write_ManyOverlappingCallsToSamePath_NeverCorruptsTheFileOrLeavesATempFileBehind() {
            // Each concurrent call used to share the single fixed temp name "path + \".tmp\"", so one
            // call's File.WriteAllText could clobber another's still-unmoved temp file before its
            // File.Move ran - one call's File.Move would then throw (its temp file was already
            // consumed) while the OTHER call's data silently landed at `path`, so the "failed" caller's
            // false return was a lie about whose data actually got written. A fresh Guid per call (see
            // Write) gives every call its own private temp file, so that specific cross-contamination
            // can no longer happen. Windows' File.Move(overwrite: true) still isn't atomic against
            // concurrent writers to the same destination, so under real contention some calls can
            // legitimately fail (that's an acceptable, honestly-reported outcome, not the bug) - what
            // this test actually verifies is that whichever call's move DOES land, the file is always
            // that one call's complete, valid content (never a corrupt mix of two calls), and that no
            // orphaned temp file is left behind by either a failed or a successful call.
            var path = CreateTempPath();
            try {
                var writes = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
                    NightFrontProgressSnapshotWriter.Write(path, new NightFrontProgressSnapshot {
                        Targets = { new NightFrontTargetProgress { Name = $"Target {i}" } }
                    })));

                var results = await Task.WhenAll(writes);

                Assert.Contains(true, results);
                Assert.True(File.Exists(path));
                var finalSnapshot = JsonConvert.DeserializeObject<NightFrontProgressSnapshot>(File.ReadAllText(path));
                Assert.NotNull(finalSnapshot);
                Assert.Single(finalSnapshot.Targets);
                Assert.Empty(SiblingTempFiles(path));
            } finally {
                File.Delete(path);
            }
        }

        private static string[] SiblingTempFiles(string path) {
            var directory = Path.GetDirectoryName(path);
            var pattern = Path.GetFileName(path) + ".*.tmp";
            return Directory.GetFiles(directory!, pattern);
        }
    }
}
