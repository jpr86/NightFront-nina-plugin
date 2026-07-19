using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Loops while at least one outstanding (not yet completed) calibration requirement remains in
    /// the accumulated calibration-metadata file - reads fresh via NightFrontMetadataStore on every
    /// check rather than tracking an iteration counter, since the nested flat instruction (NightFront
    /// Sky/Trained Flats) is what actually marks entries completed as it finishes each one.
    /// </summary>
    [ExportMetadata("Name", "NightFront While Calibration Remains")]
    [ExportMetadata("Description", "Loops while at least one calibration requirement remains in the NightFront calibration-metadata file.")]
    [ExportMetadata("Icon", "LoopSVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceCondition))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontWhileCalibrationRemainsCondition : SequenceCondition {

        [ImportingConstructor]
        public NightFrontWhileCalibrationRemainsCondition() {
        }

        private NightFrontWhileCalibrationRemainsCondition(NightFrontWhileCalibrationRemainsCondition copyMe) : this() {
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
            return NightFrontMetadataStore.PeekNext(livePath, filterOrder) != null;
        }

        public override object Clone() {
            return new NightFrontWhileCalibrationRemainsCondition(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Condition: {nameof(NightFrontWhileCalibrationRemainsCondition)}";
        }
    }
}
