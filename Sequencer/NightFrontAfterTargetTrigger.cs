using Newtonsoft.Json;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Fires right after a target inside the NightFront Container finishes - however it ended
    /// (completed, skipped, or failed) - before the sequence moves on to the next item
    /// (todos/nina-target-broadcast-plan.md). Fires even for the very last target of the night:
    /// NINA's own SequentialStrategy calls RunTriggersAfter one final time with `nextItem == null`
    /// once GetNextItem finds nothing left. Runs whatever instructions the user drops below -
    /// typically a Ground Station send using $$TARGET_NAME$$, which resolves because
    /// NightFrontTargetTriggerBase.Execute re-parents TriggerRunner onto the target's own
    /// DeepSkyObjectContainer for the duration of the run.
    ///
    /// Placement matters: this only fires for targets that actually execute within this trigger's own
    /// subtree (see NightFrontTargetTriggerBase.IsNightFrontTarget/Validate) - it must sit on the
    /// NightFront Container itself, or an ancestor of it (e.g. "Loop while safe"), not a sibling
    /// branch such as "Once Safe".
    /// </summary>
    [ExportMetadata("Name", "After Target")]
    [ExportMetadata("Description", "Fires right after a NightFront-planned target finishes (completed, skipped, or failed), before the sequence moves on. Runs whatever instructions you drop below - e.g. a Ground Station send using $$TARGET_NAME$$.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontAfterTargetTrigger : NightFrontTargetTriggerBase {

        [ImportingConstructor]
        public NightFrontAfterTargetTrigger() : base("After Target Actions") {
        }

        private NightFrontAfterTargetTrigger(NightFrontAfterTargetTrigger copyMe) : this() {
            CopyRunnerItems(copyMe);
        }

        public override object Clone() {
            return new NightFrontAfterTargetTrigger(this);
        }

        // ShouldTrigger deliberately always false - "After Target" only ever fires from
        // ShouldTriggerAfter.
        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            return false;
        }

        public override bool ShouldTriggerAfter(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (!IsNightFrontTarget(previousItem)) {
                return false;
            }
            RecordFired((DeepSkyObjectContainer)previousItem);
            return true;
        }

        public override string ToString() {
            return $"Category: {Category}, Trigger: {nameof(NightFrontAfterTargetTrigger)}";
        }
    }
}
