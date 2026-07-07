using JeffRidder.NINA.Nightfront.Import;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class NightFrontMetadataStoreTests {

        private static (string LivePath, string ArchivedPath) CreateTempPaths() {
            var id = Guid.NewGuid();
            return (
                Path.Combine(Path.GetTempPath(), id + ".metadata.json"),
                Path.Combine(Path.GetTempPath(), id + ".archived.metadata.json"));
        }

        private static void DeletePaths((string LivePath, string ArchivedPath) paths) {
            File.Delete(paths.LivePath);
            File.Delete(paths.ArchivedPath);
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
        public void TryAddCalibrationRequirement_DedupesWithinOneDegree_SameFilterGainOffset() {
            var paths = CreateTempPaths();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.4, 100, 10);

                Assert.False(added);
                Assert.Single(NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_DoesNotDedupe_WhenGainDiffers() {
            var paths = CreateTempPaths();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 200, 10);

                Assert.True(added);
                Assert.Equal(2, NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements.Count);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_DoesNotDedupe_WhenOffsetDiffers() {
            var paths = CreateTempPaths();
            try {
                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 100, 10));
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 100, 20);

                Assert.True(added);
                Assert.Equal(2, NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements.Count);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void TryAddCalibrationRequirement_RefusesToAdd_WhenEquivalentEntryAlreadyArchived() {
            var paths = CreateTempPaths();
            try {
                var claimed = NightFrontMetadataStore.ClaimNext(paths.LivePath); // no-op, nothing to claim yet
                Assert.Null(claimed);

                Assert.True(NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.0, 100, 10));
                var toArchive = NightFrontMetadataStore.ClaimNext(paths.LivePath);
                Assert.NotNull(toArchive);
                NightFrontMetadataStore.ArchiveClaimed(paths.ArchivedPath, toArchive);

                // Same (filter, angle within 1 degree, gain, offset) is already archived - must not
                // be re-added to the live file (item 2's "check both files" rule).
                var added = NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "Ha", 30.2, 100, 10);

                Assert.False(added);
                Assert.Empty(NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void ClaimNext_IsAtomic_SecondCallGetsNextEntry_NotTheSameOne() {
            var paths = CreateTempPaths();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "L", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "B", 180.0, -1, -1);

                var first = NightFrontMetadataStore.ClaimNext(paths.LivePath);
                var second = NightFrontMetadataStore.ClaimNext(paths.LivePath);
                var third = NightFrontMetadataStore.ClaimNext(paths.LivePath);

                Assert.Equal("L", first.Filter);
                Assert.Equal("B", second.Filter);
                Assert.Null(third);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void RestoreClaimed_PutsEntryBackAtHead() {
            var paths = CreateTempPaths();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "L", 180.0, -1, -1);
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "B", 180.0, -1, -1);

                var claimed = NightFrontMetadataStore.ClaimNext(paths.LivePath);
                Assert.Equal("L", claimed.Filter);

                NightFrontMetadataStore.RestoreClaimed(paths.LivePath, claimed);

                var requirements = NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements;
                Assert.Equal(2, requirements.Count);
                Assert.Equal("L", requirements[0].Filter);
                Assert.Equal("B", requirements[1].Filter);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void ArchiveClaimed_AppendsWithoutNeedingToReFindTheEntry() {
            var paths = CreateTempPaths();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "L", 180.0, -1, -1);
                var claimed = NightFrontMetadataStore.ClaimNext(paths.LivePath);

                NightFrontMetadataStore.ArchiveClaimed(paths.ArchivedPath, claimed);

                var archived = NightFrontMetadataStore.Load(paths.ArchivedPath);
                var entry = Assert.Single(archived.CalibrationRequirements);
                Assert.Equal("L", entry.Filter);
                Assert.Empty(NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void UpsertTargetMetadata_ReplacesExistingEntryByName_NotAppendingADuplicate() {
            var paths = CreateTempPaths();
            try {
                NightFrontMetadataStore.UpsertTargetMetadata(paths.LivePath, "M 16", new[] { "Ha" });
                NightFrontMetadataStore.UpsertTargetMetadata(paths.LivePath, "M 16", new[] { "Ha", "OIII" });

                var targets = NightFrontMetadataStore.Load(paths.LivePath).Targets;
                var target = Assert.Single(targets);
                Assert.Equal("M 16", target.TargetName);
                Assert.Equal(new[] { "Ha", "OIII" }, target.Filters);
            } finally {
                DeletePaths(paths);
            }
        }

        [Fact]
        public void PeekNext_DoesNotRemoveTheEntry() {
            var paths = CreateTempPaths();
            try {
                NightFrontMetadataStore.TryAddCalibrationRequirement(paths.LivePath, paths.ArchivedPath, "L", 180.0, -1, -1);

                var peeked1 = NightFrontMetadataStore.PeekNext(paths.LivePath);
                var peeked2 = NightFrontMetadataStore.PeekNext(paths.LivePath);

                Assert.Equal("L", peeked1.Filter);
                Assert.Equal("L", peeked2.Filter);
                Assert.Single(NightFrontMetadataStore.Load(paths.LivePath).CalibrationRequirements);
            } finally {
                DeletePaths(paths);
            }
        }
    }
}
