using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Loops while the next outstanding entry of the accumulated calibration-metadata file (per the
    /// configured flat filter order - see NightFrontMetadataStore.SelectNext) keeps the same rotation
    /// angle (rounded to the nearest whole degree) as the first entry seen this loop run; stops the
    /// moment that angle would differ. Since the nested flat instruction (NightFront Sky/Trained
    /// Flats) marks each entry completed as it finishes it, "the next outstanding entry" always
    /// reflects what's left to do. Reads fresh via NightFrontMetadataStore on every check rather than
    /// tracking an iteration counter.
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
            BaseName = copyMe.BaseName;
        }

        /// <summary>Which calibration-metadata file to read. Blank auto-detects the single
        /// "*.metadata.json" file in the configured NightFront data folder.</summary>
        [JsonProperty]
        public string BaseName { get; set; } = "";

        public override bool Check(ISequenceItem previousItem, ISequenceItem nextItem) {
            var folder = Settings.Default.NightFrontDataFolder;
            if (string.IsNullOrWhiteSpace(folder)) {
                return false;
            }

            var resolvedBaseName = NightFrontMetadataPaths.ResolveBaseName(folder, BaseName, out _);
            if (resolvedBaseName == null) {
                return false;
            }

            var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, resolvedBaseName);
            var filterOrder = NightFrontFilterOrder.Parse(Settings.Default.FlatFilterOrder);
            var entry = NightFrontMetadataStore.PeekNext(livePath, filterOrder);
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
            return $"Category: {Category}, Condition: {nameof(NightFrontWhileSameRotationCondition)}, BaseName: {BaseName}";
        }
    }
}
