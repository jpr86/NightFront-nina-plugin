using JeffRidder.NINA.Nightfront.Import;
using Newtonsoft.Json;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FlatDevice;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.ComponentModel.Composition;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Rotates to, and shoots trained flats for, the head entry of the accumulated
    /// calibration-metadata file: auto-fills filter/gain/offset from that entry into NINA's own
    /// Trained Flat Exposure instruction (which itself looks up the matching trained
    /// exposure/brightness value), leaving only the number of flats to collect for the user to
    /// configure. Moves the completed entry into the archive on success (item 3d of the
    /// calibration-metadata todo).
    /// </summary>
    [ExportMetadata("Name", "NightFront Trained Flats")]
    [ExportMetadata("Description", "Rotates to the next outstanding calibration requirement's mechanical angle and shoots trained flats for its filter/gain/offset, using NINA's own Trained Flat Exposure capture. Moves the completed requirement into the archive on success.")]
    [ExportMetadata("Icon", "BrainBulbSVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontTrainedFlatsInstruction : NightFrontFlatsInstructionBase {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly IFlatDeviceMediator flatDeviceMediator;

        [ImportingConstructor]
        public NightFrontTrainedFlatsInstruction(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IImageSaveMediator imageSaveMediator,
            IImageHistoryVM imageHistoryVM,
            IFilterWheelMediator filterWheelMediator,
            IRotatorMediator rotatorMediator,
            IFlatDeviceMediator flatDeviceMediator)
            : base(profileService, rotatorMediator,
                new TrainedFlatExposure(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM, filterWheelMediator, flatDeviceMediator)) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;
            this.rotatorMediator = rotatorMediator;
            this.flatDeviceMediator = flatDeviceMediator;
        }

        private NightFrontTrainedFlatsInstruction(NightFrontTrainedFlatsInstruction copyMe)
            : this(copyMe.profileService, copyMe.cameraMediator, copyMe.imagingMediator, copyMe.imageSaveMediator, copyMe.imageHistoryVM, copyMe.filterWheelMediator, copyMe.rotatorMediator, copyMe.flatDeviceMediator) {
            CopyMetaData(copyMe);
            Amount = copyMe.Amount;
        }

        [JsonProperty] public int Amount { get; set; } = 1;

        protected override void ConfigureFlatItem(NightFrontCalibrationRequirement claimed) {
            var trainedFlatExposure = (TrainedFlatExposure)flatItem;
            ApplyFilterAndExposureSettings(trainedFlatExposure.GetSwitchFilterItem(), trainedFlatExposure.GetExposureItem(), claimed);
            trainedFlatExposure.GetIterations().Iterations = Amount;
        }

        public override object Clone() {
            return new NightFrontTrainedFlatsInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontTrainedFlatsInstruction)}";
        }
    }
}
