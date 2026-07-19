using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Loops while the next outstanding entry AT THE SAME ANGLE this loop run started with (per the
    /// configured flat filter order among entries at that angle - see
    /// NightFrontMetadataStore.SelectNext's scopedToAngleDegrees) still exists; stops the moment none
    /// remain there. Since the nested flat instruction (NightFront Sky/Trained Flats) marks each entry
    /// completed as it finishes it, "the next outstanding entry at this angle" always reflects what's
    /// left to do here. Reads fresh via NightFrontMetadataStore on every check rather than tracking an
    /// iteration counter.
    ///
    /// BUG FIX: this used to call PeekNext unscoped on every check, not just the first - ranking
    /// candidates by filterOrder GLOBALLY rather than restricting to the current angle first. Once the
    /// single highest-ranked entry at the starting angle was completed, PeekNext could jump straight to
    /// a different, higher-ranked filter sitting at a COMPLETELY DIFFERENT angle (e.g. two "L"
    /// requirements at different angles, both outranking a "B" requirement genuinely at the current
    /// angle) - this condition would then (correctly, given what it was handed) see that as an angle
    /// change and stop, skipping the same-angle "B" entry that never got a turn. Confirmed against a
    /// real production run: "L" and "B" within 0.05deg of each other both outstanding, "L" completed,
    /// "B" left outstanding because an unrelated second "L" requirement ~45deg away outranked it.
    /// Fixed by scoping every check after the first to baselineRoundedAngle, so "the next outstanding
    /// entry" genuinely means "at this angle," not "overall."
    ///
    /// Applied to todos/TargetsForTonight_2026-07-06.metadata.json (L/B/OIII all ~179.9deg, rounding
    /// to 180deg; Ha ~90.1deg, rounding to 90deg; in that list order): baseline is set to 180deg from
    /// L, so L, B, and OIII are all processed (all round to 180deg) and the loop stops before Ha
    /// (rounds to 90deg) - confirmed with the maintainer as the intended, numerically-consistent
    /// behavior (the todo's own prose example has Ha/OIII transposed).
    /// </summary>
    [ExportMetadata("Name", "NightFront While Same Rotation")]
    [ExportMetadata("Description", "Loops while the next outstanding calibration requirement's rotation angle (rounded to the nearest whole degree) matches the one this loop started with; stops the moment it would differ.")]
    [ExportMetadata("Icon", "LoopSVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontWhileSameRotationCondition : SequenceCondition {
        private double? baselineRoundedAngle;

        [ImportingConstructor]
        public NightFrontWhileSameRotationCondition() {
        }

        private NightFrontWhileSameRotationCondition(NightFrontWhileSameRotationCondition copyMe) : this() {
            CopyMetaData(copyMe);
        }

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            var folder = Settings.Default.NightFrontDataFolder;
            var livePath = NightFrontMetadataPaths.ResolveExistingMetadataPath(folder, out _);
            if (livePath == null) {
                // No metadata file yet - nothing to loop over, so stop.
                return false;
            }

            var filterOrder = NightFrontFilterOrder.Parse(Settings.Default.FlatFilterOrder);
            // Unscoped on the first check of a run (baselineRoundedAngle is still null) to establish
            // the baseline from whatever's globally next-best - matches what NightFrontRotateToNext
            // AngleInstruction already rotated to just before this loop started. Scoped to that
            // baseline on every check after, so a still-outstanding entry at THIS angle is found even
            // if a higher-ranked filter now sits at a different angle (see class doc comment).
            var entry = NightFrontMetadataStore.PeekNext(livePath, filterOrder, scopedToAngleDegrees: baselineRoundedAngle);
            if (entry == null) {
                return false;
            }

            var currentRounded = Math.Round(entry.RotationAngle, MidpointRounding.AwayFromZero);
            if (baselineRoundedAngle == null) {
                baselineRoundedAngle = currentRounded;
                return true;
            }

            return currentRounded == baselineRoundedAngle;
        }

        public override void ResetProgress() {
            baselineRoundedAngle = null;
            base.ResetProgress();
        }

        public override object Clone() {
            return new NightFrontWhileSameRotationCondition(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Condition: {nameof(NightFrontWhileSameRotationCondition)}";
        }
    }
}
