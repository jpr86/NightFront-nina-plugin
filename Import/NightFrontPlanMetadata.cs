using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Filters and rotation angles actually used while executing an imported NightFront plan,
    /// written alongside the plan file so a future calibration-frame instruction can determine what
    /// calibration frames are needed for the night. Populated by NightFrontMetadataRecorder as each
    /// target's CenterAndRotate finishes - RotationAngle is the rotator's measured mechanical
    /// position after slew/center/rotate, not the plan's input Sky PA, since calibration frames need
    /// to match the physical rotator position a flat/dark frame will actually be shot at.
    /// </summary>
    public class NightFrontPlanMetadata {
        public int SchemaVersion { get; set; } = 2;

        public string Date { get; set; }

        public string SourcePlanFile { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>
        /// Distinct (filter, measured rotation angle) pairs needed for calibration frames. Deduped
        /// so a filter is never listed twice with rotation angles within 1 degree of each other.
        /// </summary>
        public List<NightFrontCalibrationRequirement> CalibrationRequirements { get; set; } = new List<NightFrontCalibrationRequirement>();

        public List<NightFrontTargetMetadata> Targets { get; set; } = new List<NightFrontTargetMetadata>();
    }

    public class NightFrontCalibrationRequirement {
        public string Filter { get; set; }

        public double RotationAngle { get; set; }
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
