using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.SequenceItem;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Loops while at least one calibration requirement remains in the accumulated
    /// calibration-metadata file - reads fresh via NightFrontMetadataStore on every check rather than
    /// tracking an iteration counter, since the nested flat instruction (NightFront Sky/Trained
    /// Flats) is what actually removes entries as it completes each one.
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
            return NightFrontMetadataStore.PeekNext(livePath) != null;
        }

        public override object Clone() {
            return new NightFrontWhileCalibrationRemainsCondition(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Condition: {nameof(NightFrontWhileCalibrationRemainsCondition)}, BaseName: {BaseName}";
        }
    }
}
