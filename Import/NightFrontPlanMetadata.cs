using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Filters, rotation angles, gain, and offset actually used while executing an imported
    /// NightFront plan, accumulated indefinitely (across nights, until a user deletes the file) so a
    /// calibration-frame instruction can determine what calibration frames are still needed.
    /// Populated by NightFrontMetadataRecorder as each target's own filter/exposure blocks finish -
    /// RotationAngle is the rotator's measured mechanical position after slew/center/rotate, not the
    /// plan's input Sky PA, since calibration frames need to match the physical rotator position a
    /// flat/dark frame will actually be shot at. All reads/writes to a file of this shape should go
    /// through NightFrontMetadataStore, not File I/O directly, since the file is shared across
    /// multiple instructions within one running sequence.
    ///
    /// There is no separate archive file: a completed requirement simply gets its
    /// FlatsCompletedDate stamped and stays in this same list, skipped by anything looking for
    /// what's still outstanding, until NightFrontMetadataStore.PruneStaleCompleted removes it once
    /// it's older than the configured refresh window.
    /// </summary>
    public class NightFrontPlanMetadata {
        public int SchemaVersion { get; set; } = 4;

        public string Date { get; set; }

        public string SourcePlanFile { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>
        /// Distinct (filter, measured rotation angle, gain, offset) combinations needed for
        /// calibration frames. Deduped so the same filter/gain/offset is never listed twice with
        /// rotation angles within 1 degree of each other.
        /// </summary>
        public List<NightFrontCalibrationRequirement> CalibrationRequirements { get; set; } = new List<NightFrontCalibrationRequirement>();

        public List<NightFrontTargetMetadata> Targets { get; set; } = new List<NightFrontTargetMetadata>();
    }

    public class NightFrontCalibrationRequirement {
        /// <summary>Stable identity used by ClaimNext/MarkCompleted/ReleaseClaim to find this exact
        /// entry again without relying on list position or value equality. Entries loaded from a file
        /// written before this field existed come back as Guid.Empty; NightFrontMetadataStore.Load
        /// assigns each of those a fresh id in memory on every load, which becomes permanent the next
        /// time anything saves the file.</summary>
        public Guid Id { get; set; }

        public string Filter { get; set; }

        public double RotationAngle { get; set; }

        /// <summary>-1 sentinel (same convention as TakeExposure.Gain) means "camera/profile default."
        /// Missing on an older (SchemaVersion 2) file, which Json.NET defaults to -1 on load via this
        /// property initializer - such entries never dedupe against a real gain, so they stay in the
        /// list until a flat instruction consumes them or a user clears the file.</summary>
        public int Gain { get; set; } = -1;

        /// <summary>-1 sentinel, same convention as TakeExposure.Offset.</summary>
        public int Offset { get; set; } = -1;

        /// <summary>When this requirement was first added. No default initializer, so an entry from a
        /// file written before this field existed loads as DateTime.MinValue (stable across reloads)
        /// rather than a misleading "now."</summary>
        public DateTime DateAdded { get; set; }

        /// <summary>Null until a NightFront Sky/Trained Flats instruction successfully completes this
        /// requirement. Once set, this entry is skipped by anything looking for what's still
        /// outstanding, but stays in the file - see NightFrontMetadataStore.PruneStaleCompleted for
        /// how/when it eventually gets removed.</summary>
        public DateTime? FlatsCompletedDate { get; set; }

        /// <summary>True while a flats instruction currently has this entry checked out via
        /// ClaimNext, so a second concurrent caller (e.g. two sequence branches sharing one metadata
        /// file) can't claim the same entry. Cleared by MarkCompleted on success or ReleaseClaim on
        /// failure/cancellation.</summary>
        public bool Claimed { get; set; }
    }

    public class NightFrontTargetMetadata {
        public string TargetName { get; set; }

        /// <summary>
        /// The rotator's measured mechanical position after this target's CenterAndRotate finished,
        /// or null if that hasn't happened yet (e.g. the sequence hasn't reached this target, or it
        /// stopped before this target's rotation completed).
        /// </summary>
        public double? MeasuredRotationAngle { get; set; }

        /// <summary>Distinct filter names planned for this target.</summary>
        public List<string> Filters { get; set; } = new List<string>();
    }
}
