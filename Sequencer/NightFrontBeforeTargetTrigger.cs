using Newtonsoft.Json;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Fires just before a target inside the NightFront Container starts - before that target's own
    /// slew/centering, since CenterAndRotate runs inside the target's own DeepSkyObjectContainer.
    /// "Starting M31" therefore precedes the first photon by minutes and still fires even if
    /// centering later fails; that's the right meaning for "target begins". Runs whatever
    /// instructions the user drops below - typically a Ground Station send using
    /// $$TARGET_NAME$$/$$TARGET_RA$$/$$TARGET_DEC$$, which
    /// resolve because NightFrontTargetTriggerBase.Execute re-parents TriggerRunner onto the target's
    /// own DeepSkyObjectContainer for the duration of the run.
    ///
    /// Placement matters: this only fires for targets that actually execute within this trigger's own
    /// subtree (see NightFrontTargetTriggerBase.IsNightFrontTarget/Validate) - it must sit on the
    /// NightFront Container itself, or an ancestor of it (e.g. "Loop while safe"), not a sibling
    /// branch such as "Once Safe".
    /// </summary>
    [ExportMetadata("Name", "Before Target")]
    [ExportMetadata("Description", "Fires just before a NightFront-planned target starts (before its own slew/centering). Runs whatever instructions you drop below - e.g. a Ground Station send using $$TARGET_NAME$$/$$TARGET_RA$$/$$TARGET_DEC$$.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontBeforeTargetTrigger : NightFrontTargetTriggerBase {

        [ImportingConstructor]
        public NightFrontBeforeTargetTrigger() : base("Before Target Actions") {
        }

        private NightFrontBeforeTargetTrigger(NightFrontBeforeTargetTrigger copyMe) : this() {
            CopyRunnerItems(copyMe);
        }

        public override object Clone() {
            return new NightFrontBeforeTargetTrigger(this);
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (!IsNightFrontTarget(nextItem)) {
                return false;
            }
            RecordFired((DeepSkyObjectContainer)nextItem);
            return true;
        }

        // ShouldTriggerAfter deliberately inherits SequenceTrigger's `false` default - "Before
        // Target" only ever fires from ShouldTrigger.

        public override string ToString() {
            return $"Category: {Category}, Trigger: {nameof(NightFrontBeforeTargetTrigger)}";
        }
    }
}
