using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using JeffRidder.NINA.Nightfront.Sequencer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    // Settings.Default.NightFrontDataFolder is a process-wide static, so each test saves/restores it.
    [Collection("NightFrontSettings")]
    public class NightFrontWhileSameRotationConditionTests {

        private static string CreateTempFolder() {
            var folder = Path.Combine(Path.GetTempPath(), "NightFrontTests_" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Mirrors todos/TargetsForTonight_2026-07-06.metadata.json: L/B/OIII all round to 180 degrees,
        // Ha rounds to 90 degrees, in that list order.
        [Fact]
        public void Check_StopsWhenRoundedAngleChanges_ProcessingLBAndOIII_StoppingBeforeHa() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder);

                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "L", 179.9295654296875, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "B", 179.9295654296875, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "OIII", 179.89825439453125, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "Ha", 90.14373016357422, -1, -1);

                var condition = new NightFrontWhileSameRotationCondition();
                var noFilterOrder = new List<string>();

                // Check before processing L: establishes the baseline (180deg) and allows the pass.
                Assert.True(condition.Check(null, null));
                NightFrontMetadataStore.MarkCompleted(livePath, NightFrontMetadataStore.ClaimNext(livePath, noFilterOrder).Id); // "process" L

                Assert.True(condition.Check(null, null)); // B still rounds to 180
                NightFrontMetadataStore.MarkCompleted(livePath, NightFrontMetadataStore.ClaimNext(livePath, noFilterOrder).Id); // "process" B

                Assert.True(condition.Check(null, null)); // OIII still rounds to 180
                NightFrontMetadataStore.MarkCompleted(livePath, NightFrontMetadataStore.ClaimNext(livePath, noFilterOrder).Id); // "process" OIII

                Assert.False(condition.Check(null, null)); // Ha rounds to 90 - stop before processing it

                var remaining = NightFrontMetadataStore.Load(livePath).CalibrationRequirements.Where(r => r.FlatsCompletedDate == null);
                var remainingEntry = Assert.Single(remaining);
                Assert.Equal("Ha", remainingEntry.Filter);
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }

        // Reproduces a real production bug from an actual TargetsForTonight.metadata.json: a "B"
        // requirement and an "L" requirement within 0.05deg of each other (both ~180deg), plus a
        // second, unrelated "L" requirement at a completely different angle (~135deg), with
        // FlatFilterOrder ranking "L" ahead of "B". Before the fix, Check() called PeekNext unscoped
        // on every call - once the first "L" (180deg) was processed, the globally next-best entry by
        // filterOrder was the SECOND "L" at 135deg (still outranking "B"), which didn't match the
        // 180deg baseline, so Check() incorrectly returned false and the loop stopped before "B" -
        // which was genuinely still outstanding at the current 180deg angle - ever got processed.
        [Fact]
        public void Check_ContinuesToLowerRankedFilterAtSameAngle_BeforeMovingToHigherRankedFilterAtDifferentAngle() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var originalFilterOrder = Settings.Default.FlatFilterOrder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                Settings.Default.FlatFilterOrder = "L, B";
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder);

                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "B", 179.97581481933594, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "L", 179.9295654296875, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "L", 134.90821838378906, -1, -1);

                var condition = new NightFrontWhileSameRotationCondition();
                var filterOrder = new List<string> { "L", "B" };

                // Establishes the baseline (180deg) from the globally-next-best entry - "L" outranks
                // "B" and is the earlier-listed of the two "L"s.
                Assert.True(condition.Check(null, null));
                var first = NightFrontMetadataStore.ClaimNext(livePath, filterOrder, scopedToAngleDegrees: 180.0);
                Assert.Equal("L", first.Filter);
                NightFrontMetadataStore.MarkCompleted(livePath, first.Id);

                // "B" is still outstanding at 180deg - THIS is the check that used to incorrectly
                // return false (jumping to the second "L" at 135deg instead).
                Assert.True(condition.Check(null, null));
                var second = NightFrontMetadataStore.ClaimNext(livePath, filterOrder, scopedToAngleDegrees: 180.0);
                Assert.Equal("B", second.Filter);
                NightFrontMetadataStore.MarkCompleted(livePath, second.Id);

                // Now nothing remains at 180deg - the loop correctly stops, leaving the second "L"
                // (135deg) for the outer loop to rotate to and pick up separately.
                Assert.False(condition.Check(null, null));

                var remaining = NightFrontMetadataStore.Load(livePath).CalibrationRequirements.Where(r => r.FlatsCompletedDate == null);
                var remainingEntry = Assert.Single(remaining);
                Assert.Equal("L", remainingEntry.Filter);
                Assert.Equal(134.90821838378906, remainingEntry.RotationAngle);
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
                Settings.Default.FlatFilterOrder = originalFilterOrder;
            }
        }

        [Fact]
        public void Check_ReturnsFalse_WhenNoCalibrationRequirementsRemain() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                var condition = new NightFrontWhileSameRotationCondition();

                Assert.False(condition.Check(null, null));
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }

        [Fact]
        public void ResetProgress_ReestablishesFreshBaselineOnNextCheck() {
            var originalFolder = Settings.Default.NightFrontDataFolder;
            var folder = CreateTempFolder();
            try {
                Settings.Default.NightFrontDataFolder = folder;
                var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "Ha", 90.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, "L", 180.0, -1, -1);

                var condition = new NightFrontWhileSameRotationCondition();
                var noFilterOrder = new List<string>();
                Assert.True(condition.Check(null, null)); // baseline = 90 (Ha)
                NightFrontMetadataStore.MarkCompleted(livePath, NightFrontMetadataStore.ClaimNext(livePath, noFilterOrder).Id);

                Assert.False(condition.Check(null, null)); // L (180) != baseline (90)

                condition.ResetProgress();

                Assert.True(condition.Check(null, null)); // fresh baseline = 180 (L)
            } finally {
                Directory.Delete(folder, recursive: true);
                Settings.Default.NightFrontDataFolder = originalFolder;
            }
        }
    }
}
