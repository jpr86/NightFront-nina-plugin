using JeffRidder.NINA.Nightfront.Import;
using Newtonsoft.Json;
using NINA.Astrometry.Interfaces;
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
    /// Rotates to, and shoots twilight sky flats for, the head entry of the accumulated
    /// calibration-metadata file: auto-fills filter/gain/offset from that entry into NINA's own
    /// SkyFlat instruction, leaving min/max exposure, histogram mean target/tolerance, dither, and
    /// amount for the user to configure. Moves the completed entry into the archive on success (item
    /// 3d of the calibration-metadata todo).
    /// </summary>
    [ExportMetadata("Name", "NightFront Sky Flats")]
    [ExportMetadata("Description", "Rotates to the next outstanding calibration requirement's mechanical angle and shoots twilight sky flats for its filter/gain/offset, using NINA's own Twilight Sky Flats capture. Moves the completed requirement into the archive on success.")]
    [ExportMetadata("Icon", "FlatWizardSVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontSkyFlatsInstruction : NightFrontFlatsInstructionBase {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly ITwilightCalculator twilightCalculator;

        [ImportingConstructor]
        public NightFrontSkyFlatsInstruction(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            ITelescopeMediator telescopeMediator,
            IImagingMediator imagingMediator,
            IImageSaveMediator imageSaveMediator,
            IImageHistoryVM imageHistoryVM,
            IFilterWheelMediator filterWheelMediator,
            IRotatorMediator rotatorMediator,
            ITwilightCalculator twilightCalculator)
            : base(profileService, rotatorMediator,
                new SkyFlat(profileService, cameraMediator, telescopeMediator, imagingMediator, imageSaveMediator, imageHistoryVM, filterWheelMediator, twilightCalculator)) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;
            this.rotatorMediator = rotatorMediator;
            this.twilightCalculator = twilightCalculator;
        }

        private NightFrontSkyFlatsInstruction(NightFrontSkyFlatsInstruction copyMe)
            : this(copyMe.profileService, copyMe.cameraMediator, copyMe.telescopeMediator, copyMe.imagingMediator, copyMe.imageSaveMediator, copyMe.imageHistoryVM, copyMe.filterWheelMediator, copyMe.rotatorMediator, copyMe.twilightCalculator) {
            CopyMetaData(copyMe);
            BaseName = copyMe.BaseName;
            MinExposure = copyMe.MinExposure;
            MaxExposure = copyMe.MaxExposure;
            HistogramTargetPercentage = copyMe.HistogramTargetPercentage;
            HistogramTolerancePercentage = copyMe.HistogramTolerancePercentage;
            ShouldDither = copyMe.ShouldDither;
            Amount = copyMe.Amount;
        }

        [JsonProperty] public double MinExposure { get; set; }

        [JsonProperty] public double MaxExposure { get; set; } = 10;

        [JsonProperty] public double HistogramTargetPercentage { get; set; } = 0.4;

        [JsonProperty] public double HistogramTolerancePercentage { get; set; } = 0.1;

        [JsonProperty] public bool ShouldDither { get; set; }

        [JsonProperty] public int Amount { get; set; } = 20;

        protected override void ConfigureFlatItem(NightFrontCalibrationRequirement claimed) {
            var skyFlat = (SkyFlat)flatItem;
            ApplyFilterAndExposureSettings(skyFlat.GetSwitchFilterItem(), skyFlat.GetExposureItem(), claimed);
            skyFlat.MinExposure = MinExposure;
            skyFlat.MaxExposure = MaxExposure;
            skyFlat.HistogramTargetPercentage = HistogramTargetPercentage;
            skyFlat.HistogramTolerancePercentage = HistogramTolerancePercentage;
            skyFlat.ShouldDither = ShouldDither;
            skyFlat.GetIterations().Iterations = Amount;
        }

        public override object Clone() {
            return new NightFrontSkyFlatsInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontSkyFlatsInstruction)}, BaseName: {BaseName}";
        }
    }
}
