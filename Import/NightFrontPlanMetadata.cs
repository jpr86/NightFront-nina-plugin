using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Filters and rotation angles used by an imported NightFront plan, written alongside the plan
    /// file so a future calibration-frame instruction can determine what calibration frames are
    /// needed for the night. Derived from the already-imported plan object graph (what was
    /// scheduled), not from runtime execution/image-save events.
    /// </summary>
    public class NightFrontPlanMetadata {
        public int SchemaVersion { get; set; } = 1;

        public string Date { get; set; }

        public string SourcePlanFile { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>Distinct filter names used anywhere in the plan.</summary>
        public List<string> Filters { get; set; } = new List<string>();

        /// <summary>Distinct rotation angles used anywhere in the plan.</summary>
        public List<double> RotationAngles { get; set; } = new List<double>();

        public List<NightFrontTargetMetadata> Targets { get; set; } = new List<NightFrontTargetMetadata>();
    }

    public class NightFrontTargetMetadata {
        public string TargetName { get; set; }

        /// <summary>Distinct rotation angles used by this target (its own PositionAngle plus any
        /// CenterAndRotate angles found in its subtree).</summary>
        public List<double> RotationAngles { get; set; } = new List<double>();

        /// <summary>Distinct filter names used by this target.</summary>
        public List<string> Filters { get; set; } = new List<string>();
    }
}
