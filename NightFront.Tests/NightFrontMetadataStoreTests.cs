using JeffRidder.NINA.Nightfront.Import;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontMetadataStoreTests {

        private static readonly List<string> NoFilterOrder = new List<string>();

        private static string CreateTempPath() {
            return Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptyInstance() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");

            var metadata = NightFrontMetadataStore.Load(path);

            Assert.NotNull(metadata);
            Assert.Empty(metadata.CalibrationRequirements);
            Assert.Empty(metadata.Targets);
        }

        [Fact]
        public void Load_OldSchemaFileWithoutGainOffset_DefaultsToMinusOne() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                var oldSchemaJson = @"{
                  ""SchemaVersion"": 2,
                  ""CalibrationRequirements"": [ { ""Filter"": ""Ha"", ""RotationAngle"": 90.1 } ],
                  ""Targets"": []
                }";
                File.WriteAllText(path, oldSchemaJson);

                var metadata = NightFrontMetadataStore.Load(path);

                var requirement = Assert.Single(metadata.CalibrationRequirements);
                Assert.Equal(-1, requirement.Gain);
                Assert.Equal(-1, requirement.Offset);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Load_OldSchemaFileWithoutId_AssignsANonEmptyId() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".metadata.json");
            try {
                var oldSchemaJson = @"{
                  ""SchemaVersion"": 3,
                  ""CalibrationRequirements"": [ { ""Filter"": ""Ha"", ""RotationAngle"": 90.1, ""Gain"": -1, ""Offset"": -1 } ],
                  ""Targets"": []
                }";
                File.WriteAllText(path, oldSchemaJson);

                var metadata = NightFrontMetadataStore.Load(path);

                var requirement = Assert.Single(metadata.CalibrationRequirements);
                Assert.NotEqual(Guid.Empty, requirement.Id);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_DedupesWithinOneDegree_SameFilterGainOffset() {
            var path = CreateTempPath();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.4, 100, 10);

                Assert.False(added);
                Assert.Single(NightFrontMetadataStore.Load(path).CalibrationRequirements);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_DoesNotDedupe_WhenGainDiffers() {
            var path = CreateTempPath();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 200, 10);

                Assert.True(added);
                Assert.Equal(2, NightFrontMetadataStore.Load(path).CalibrationRequirements.Count);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_DoesNotDedupe_WhenOffsetDiffers() {
            var path = CreateTempPath();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 20);

                Assert.True(added);
                Assert.Equal(2, NightFrontMetadataStore.Load(path).CalibrationRequirements.Count);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_SetsIdAndDateAdded() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 10);

                var requirement = Assert.Single(NightFrontMetadataStore.Load(path).CalibrationRequirements);
                Assert.NotEqual(Guid.Empty, requirement.Id);
                Assert.True(requirement.DateAdded > DateTime.Now.AddMinutes(-1));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_RefusesToAdd_WhenEquivalentEntryAlreadyCompleted() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.0, 100, 10);
                var claimed = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);
                Assert.NotNull(claimed);
                NightFrontMetadataStore.MarkCompleted(path, claimed.Id);

                // Same (filter, angle within 1 degree, gain, offset) is already completed - must not
                // be re-added, since completed entries stay in the same file rather than being
                // archived elsewhere.
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 30.2, 100, 10);

                Assert.False(added);
                Assert.Single(NightFrontMetadataStore.Load(path).CalibrationRequirements);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_IsAtomic_SecondCallGetsNextEntry_NotTheSameOne() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "B", 180.0, -1, -1);

                var first = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);
                var second = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);
                var third = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);

                Assert.Equal("L", first.Filter);
                Assert.Equal("B", second.Filter);
                Assert.Null(third);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReleaseClaim_MakesEntryClaimableAgain() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "B", 180.0, -1, -1);

                var claimed = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);
                Assert.Equal("L", claimed.Filter);

                NightFrontMetadataStore.ReleaseClaim(path, claimed.Id);

                var reclaimed = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);
                Assert.Equal("L", reclaimed.Filter);

                var requirements = NightFrontMetadataStore.Load(path).CalibrationRequirements;
                Assert.Equal(2, requirements.Count);
                Assert.Null(requirements.First(r => r.Filter == "L").FlatsCompletedDate);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void MarkCompleted_StampsFlatsCompletedDate_AndEntryStaysInTheFile() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);
                var claimed = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder);

                NightFrontMetadataStore.MarkCompleted(path, claimed.Id);

                var requirement = Assert.Single(NightFrontMetadataStore.Load(path).CalibrationRequirements);
                Assert.Equal("L", requirement.Filter);
                Assert.NotNull(requirement.FlatsCompletedDate);
                Assert.False(requirement.Claimed);
                Assert.Null(NightFrontMetadataStore.PeekNext(path, NoFilterOrder));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void PruneStaleCompleted_RemovesOnlyCompletedEntriesOlderThanRefreshDays() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Stale", 10.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Recent", 20.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Pending", 30.0, -1, -1);

                var metadata = NightFrontMetadataStore.Load(path);
                metadata.CalibrationRequirements.First(r => r.Filter == "Stale").FlatsCompletedDate = DateTime.Now.AddDays(-31);
                metadata.CalibrationRequirements.First(r => r.Filter == "Recent").FlatsCompletedDate = DateTime.Now.AddDays(-1);
                File.WriteAllText(path, JsonConvert.SerializeObject(metadata));

                NightFrontMetadataStore.PruneStaleCompleted(path, refreshDays: 30);

                var remaining = NightFrontMetadataStore.Load(path).CalibrationRequirements;
                Assert.Equal(2, remaining.Count);
                Assert.Contains(remaining, r => r.Filter == "Recent");
                Assert.Contains(remaining, r => r.Filter == "Pending");
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void UpsertTargetMetadata_ReplacesExistingEntryByName_NotAppendingADuplicate() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.UpsertTargetMetadata(path, "M 16", new[] { "Ha" });
                NightFrontMetadataStore.UpsertTargetMetadata(path, "M 16", new[] { "Ha", "OIII" });

                var targets = NightFrontMetadataStore.Load(path).Targets;
                var target = Assert.Single(targets);
                Assert.Equal("M 16", target.TargetName);
                Assert.Equal(new[] { "Ha", "OIII" }, target.Filters);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void PeekNext_DoesNotRemoveOrClaimTheEntry() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);

                var peeked1 = NightFrontMetadataStore.PeekNext(path, NoFilterOrder);
                var peeked2 = NightFrontMetadataStore.PeekNext(path, NoFilterOrder);

                Assert.Equal("L", peeked1.Filter);
                Assert.Equal("L", peeked2.Filter);
                Assert.Single(NightFrontMetadataStore.Load(path).CalibrationRequirements);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_HonorsFilterOrder_OverInsertionOrder() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "OIII", 180.0, -1, -1);

                var filterOrder = new List<string> { "L", "OIII", "Ha" };

                Assert.Equal("L", NightFrontMetadataStore.ClaimNext(path, filterOrder).Filter);
                Assert.Equal("OIII", NightFrontMetadataStore.ClaimNext(path, filterOrder).Filter);
                Assert.Equal("Ha", NightFrontMetadataStore.ClaimNext(path, filterOrder).Filter);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_FiltersNotInOrderList_RankAfterListedFilters() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);

                // "L" was added first but isn't in the filter order, so it ranks after "Ha", which is
                // explicitly listed - "Ha" is claimed first even though it was added second.
                var filterOrder = new List<string> { "Ha" };

                Assert.Equal("Ha", NightFrontMetadataStore.ClaimNext(path, filterOrder).Filter);
                Assert.Equal("L", NightFrontMetadataStore.ClaimNext(path, filterOrder).Filter);
            } finally {
                File.Delete(path);
            }
        }

        // ── scopedToAngleDegrees ────────────────────────────────────────────────────────────────

        [Fact]
        public void PeekNext_ScopedToAngle_IgnoresOutstandingEntriesAtOtherAngles() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 90.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);

                var scoped = NightFrontMetadataStore.PeekNext(path, NoFilterOrder, scopedToAngleDegrees: 90.0);

                Assert.Equal("Ha", scoped.Filter);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void PeekNext_ScopedToAngle_ReturnsNull_WhenNothingOutstandingAtThatAngle() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);

                // Something is outstanding overall (at 180deg), but nothing at 90deg specifically -
                // this is what lets NightFrontWhileSameRotationCondition tell "nothing left at this
                // angle" apart from "nothing left at all."
                var scoped = NightFrontMetadataStore.PeekNext(path, NoFilterOrder, scopedToAngleDegrees: 90.0);

                Assert.Null(scoped);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void PeekNext_ScopedToAngle_UsesRoundedDegreeEquality() {
            var path = CreateTempPath();
            try {
                // Rounds to 180, same as a scope of 180.0 or 179.6 (which also rounds to 180).
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 179.7, -1, -1);

                Assert.NotNull(NightFrontMetadataStore.PeekNext(path, NoFilterOrder, scopedToAngleDegrees: 180.0));
                Assert.NotNull(NightFrontMetadataStore.PeekNext(path, NoFilterOrder, scopedToAngleDegrees: 179.6));
                // Rounds to 181, a different whole degree - out of scope even though the raw values
                // are barely 0.7deg apart.
                Assert.Null(NightFrontMetadataStore.PeekNext(path, NoFilterOrder, scopedToAngleDegrees: 180.6));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_ScopedToAngle_OnlyClaimsFromThatAngle() {
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "Ha", 90.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 180.0, -1, -1);

                var claimed = NightFrontMetadataStore.ClaimNext(path, NoFilterOrder, scopedToAngleDegrees: 180.0);

                Assert.Equal("L", claimed.Filter);
                // "Ha" at 90deg must still be outstanding, not claimed.
                Assert.False(NightFrontMetadataStore.Load(path).CalibrationRequirements.Single(r => r.Filter == "Ha").Claimed);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_ScopedToAngle_RegressionForRealProductionBug_HigherRankedFilterAtADifferentAngleDoesNotJumpTheQueue() {
            // Reproduces a real production metadata file: two "L" requirements at very different
            // angles plus a "B" requirement within 1 degree of the first "L," with filterOrder
            // ranking "L" ahead of "B". Before scopedToAngleDegrees existed, claiming/peeking after
            // the first "L" completed would jump straight to the second "L" (globally next-best by
            // filterOrder) even though "B" was legitimately next at the CURRENT angle - the
            // surrounding NightFrontWhileSameRotationCondition loop would then see that as an angle
            // change and stop, leaving "B" never shot. Scoping every claim/peek to the current angle
            // fixes this: "B" is found before the loop ever considers moving to the second "L"'s angle.
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "B", 179.97581481933594, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 179.9295654296875, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 134.90821838378906, -1, -1);
                var filterOrder = new List<string> { "L", "B" };

                // NightFrontRotateToNextAngleInstruction picks the globally-next-best (unscoped) entry
                // and physically rotates there first - that's "L" at ~180deg.
                var rotateTo = NightFrontMetadataStore.PeekNext(path, filterOrder);
                Assert.Equal("L", rotateTo.Filter);
                var currentAngle = Math.Round(rotateTo.RotationAngle, MidpointRounding.AwayFromZero);

                // The flats instruction claims scoped to that angle, shoots it, marks it complete.
                var firstClaim = NightFrontMetadataStore.ClaimNext(path, filterOrder, scopedToAngleDegrees: currentAngle);
                Assert.Equal("L", firstClaim.Filter);
                Assert.Equal(180.0, currentAngle);
                NightFrontMetadataStore.MarkCompleted(path, firstClaim.Id);

                // The loop condition re-checks, scoped to the SAME angle - must find "B", not fail to
                // find anything (which is what an unscoped PeekNext would have done here, since the
                // second "L" at 135deg outranks "B" globally).
                var stillAtThisAngle = NightFrontMetadataStore.PeekNext(path, filterOrder, scopedToAngleDegrees: currentAngle);
                Assert.NotNull(stillAtThisAngle);
                Assert.Equal("B", stillAtThisAngle.Filter);

                // The flats instruction claims and completes it too.
                var secondClaim = NightFrontMetadataStore.ClaimNext(path, filterOrder, scopedToAngleDegrees: currentAngle);
                Assert.Equal("B", secondClaim.Filter);
                NightFrontMetadataStore.MarkCompleted(path, secondClaim.Id);

                // Now nothing remains at this angle - the loop correctly stops.
                Assert.Null(NightFrontMetadataStore.PeekNext(path, filterOrder, scopedToAngleDegrees: currentAngle));

                // But the second "L" at 135deg is still outstanding overall, for the outer loop to
                // rotate to next.
                var remaining = NightFrontMetadataStore.PeekNext(path, filterOrder);
                Assert.Equal("L", remaining.Filter);
                Assert.Equal(135.0, Math.Round(remaining.RotationAngle, MidpointRounding.AwayFromZero));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ClaimNext_ScopedToAngle_FallsBackToUnscoped_WhenCallerChoosesTo_ReturnsNullOtherwise() {
            // ClaimNext/PeekNext themselves never silently fall back to a different angle when scoped
            // - that composition (try scoped, then fall back to unscoped) is the caller's own
            // responsibility (see NightFrontFlatsInstructionBase.Execute), so this just confirms the
            // scoped call alone returns null rather than reaching across to a different angle.
            var path = CreateTempPath();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(path, "L", 45.0, -1, -1);

                Assert.Null(NightFrontMetadataStore.ClaimNext(path, NoFilterOrder, scopedToAngleDegrees: 180.0));
                Assert.NotNull(NightFrontMetadataStore.ClaimNext(path, NoFilterOrder));
            } finally {
                File.Delete(path);
            }
        }
    }
}
