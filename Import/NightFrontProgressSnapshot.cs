using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// A point-in-time snapshot of how far tonight's imported plan has actually gotten, one row per
    /// top-level target in a NightFrontContainer. Built by NightFrontContainer.BuildProgressSnapshot
    /// from NightFrontTargetSummary's already-live PlannedCount/CompletedCount/Status - this adds no
    /// new progress-tracking mechanism of its own, only a serializable view of data the sequencer
    /// UI's per-target summary rows already compute (see todos/nina-safety-delay-plan.md, Finding 7 /
    /// Phase 1). Intended to be handed to a future safety-recovery replan step (Phase 3) so a
    /// remainder-of-night re-solve knows what's already been shot.
    ///
    /// CompletedCount/PlannedCount are per-target totals, not broken down by filter - the same
    /// granularity NightFrontTargetSummary itself tracks today. A consumer that needs a per-filter
    /// remainder (e.g. to resume a partially-shot target's exact outstanding subframes per filter)
    /// will need a separate enhancement; this snapshot doesn't attempt that yet.
    /// </summary>
    public class NightFrontProgressSnapshot {
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

        public List<NightFrontTargetProgress> Targets { get; set; } = new List<NightFrontTargetProgress>();
    }

    public class NightFrontTargetProgress {
        public string Name { get; set; }

        /// <summary>Total exposures planned for this target tonight, across every filter/segment -
        /// the sum of every loop's Iterations plus every unlooped single exposure, mirroring
        /// NightFrontTargetSummary.PlannedCount. Null means this target's progress could not be
        /// determined (its NightFrontTargetSummary row failed to build, or it's a top-level imported
        /// item that isn't a DeepSkyObjectContainer at all) - distinct from a real, known count of 0,
        /// and distinct from the target being entirely absent from Targets (never part of tonight's
        /// plan).</summary>
        public int? PlannedCount { get; set; }

        /// <summary>Total exposures actually completed so far, mirroring
        /// NightFrontTargetSummary.CompletedCount. Null under the same "progress unknown" condition as
        /// PlannedCount.</summary>
        public int? CompletedCount { get; set; }

        /// <summary>The target container's own NINA execution status (e.g. "CREATED", "RUNNING",
        /// "FINISHED") at the moment the snapshot was built, from SequenceEntityStatus.ToString() -
        /// CREATED with CompletedCount == 0 means this target hasn't started yet tonight. Populated
        /// even when PlannedCount/CompletedCount are null, since the container's own status is known
        /// regardless of whether exposure-level progress could be determined.</summary>
        public string Status { get; set; }
    }
}
