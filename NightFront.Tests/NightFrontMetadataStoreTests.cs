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
    }
}
