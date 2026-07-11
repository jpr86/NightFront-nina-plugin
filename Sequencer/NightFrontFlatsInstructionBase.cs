using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Rotator;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Shared orchestration for the two calibration-flat instructions (NightFront Sky Flats /
    /// NightFront Trained Flats): both rotate to, then shoot flats for, the next outstanding entry of
    /// the accumulated calibration-metadata file (picked per the configured flat filter order - see
    /// NightFrontMetadataStore.SelectNext), then stamp that entry's FlatsCompletedDate on success or
    /// clear its claim on failure/cancellation so another attempt can pick it up again.
    ///
    /// The rotator child and the wrapped NINA flat instruction (SkyFlat/TrainedFlatExposure) are real,
    /// persistent children added via Add() in the constructor - not built-and-discarded per run - so
    /// they get a real Parent and NINA's normal live per-step status/progress rendering, the same way
    /// NINA's own SkyFlat/TrainedFlatExposure pre-build their own fixed children rather than exposing
    /// them to generic engine iteration. Decompiling NINA's own SequenceItem confirmed that Run (not
    /// Execute) performs Status transitions (CREATED -> RUNNING -> FINISHED/FAILED) and that Run
    /// no-ops unless Status is CREATED, so each child is explicitly ResetProgress()'d immediately
    /// before Run() is called on it here - calling Execute() directly, as an initial draft of this
    /// class did, would silently leave the children's Status untouched and break their live UI.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class NightFrontFlatsInstructionBase : SequentialContainer, IImmutableContainer, IValidatable {
        private readonly IProfileService profileService;
        private readonly IRotatorMediator rotatorMediator;
        protected readonly MoveRotatorMechanical rotateItem;
        protected readonly SequenceItem flatItem;

        protected NightFrontFlatsInstructionBase(IProfileService profileService, IRotatorMediator rotatorMediator, SequenceItem flatItem) : base() {
            this.profileService = profileService;
            this.rotatorMediator = rotatorMediator;
            rotateItem = new MoveRotatorMechanical(rotatorMediator);
            this.flatItem = flatItem;
            Add(rotateItem);
            Add(this.flatItem);
        }

        /// <summary>Which calibration-metadata file to claim from. Blank auto-detects the single
        /// "*.metadata.json" file in the configured NightFront data folder.</summary>
        [JsonProperty]
        public string BaseName { get; set; } = "";

        /// <summary>Validates the wrapped rotate/flat children (via the inherited container
        /// validation) plus this class's own folder/BaseName/outstanding-requirement checks,
        /// appending to the same inherited Issues list the sequencer UI already displays.</summary>
        public override bool Validate() {
            var baseValid = base.Validate();

            var extraIssues = new List<string>();
            var folder = Settings.Default.NightFrontDataFolder;
            if (string.IsNullOrWhiteSpace(folder)) {
                extraIssues.Add("The NightFront data folder is not configured. Set it on the NightFront plugin's Options tab.");
            } else {
                var resolvedBaseName = NightFrontMetadataPaths.ResolveBaseName(folder, BaseName, out var issue);
                if (resolvedBaseName == null) {
                    extraIssues.Add(issue);
                } else {
                    var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, resolvedBaseName);
                    var filterOrder = NightFrontFilterOrder.Parse(Settings.Default.FlatFilterOrder);
                    if (NightFrontMetadataStore.PeekNext(livePath, filterOrder) == null) {
                        extraIssues.Add("No calibration requirements remain in the NightFront calibration-metadata file.");
                    }
                }
            }

            if (extraIssues.Count > 0) {
                Issues = (Issues ?? new List<string>()).Concat(extraIssues).ToList();
                return false;
            }

            return baseValid;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var folder = Settings.Default.NightFrontDataFolder;
            var resolvedBaseName = NightFrontMetadataPaths.ResolveBaseName(folder, BaseName, out var issue);
            if (resolvedBaseName == null) {
                throw new InvalidOperationException(issue);
            }

            var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, resolvedBaseName);
            var filterOrder = NightFrontFilterOrder.Parse(Settings.Default.FlatFilterOrder);

            // Prefer whatever's still outstanding at the rotator's current physical angle over the
            // globally-next-best-by-filterOrder entry (which could sit at a different angle entirely -
            // see NightFrontWhileSameRotationCondition's doc comment for the real bug this fixes: a
            // higher-ranked filter at a different angle would otherwise jump the queue ahead of a
            // lower-ranked filter genuinely sitting at the angle already rotated to, leaving that
            // lower-ranked filter's flats never shot). Falls back to the unscoped, globally-next-best
            // claim - today's only behavior - when the rotator's position can't be read, or when
            // nothing remains at the current angle at all (this instruction used standalone without a
            // preceding rotate-to-next-angle step, or a same-rotation loop that's genuinely exhausted
            // the current angle and needs to move on).
            double? currentAngle = null;
            try {
                currentAngle = rotatorMediator?.GetInfo()?.MechanicalPosition;
            } catch (Exception) {
                // Best-effort - fall through to the unscoped claim below.
            }

            var claimed = (currentAngle.HasValue ? NightFrontMetadataStore.ClaimNext(livePath, filterOrder, currentAngle) : null)
                ?? NightFrontMetadataStore.ClaimNext(livePath, filterOrder)
                ?? throw new InvalidOperationException("No calibration requirements remain in the NightFront calibration-metadata file.");

            try {
                rotateItem.MechanicalPosition = (float)Math.Round(claimed.RotationAngle, MidpointRounding.AwayFromZero);
                ConfigureFlatItem(claimed);

                rotateItem.ResetProgress();
                await rotateItem.Run(progress, token);
                if (rotateItem.Status == SequenceEntityStatus.FAILED) {
                    throw new InvalidOperationException("NightFront: rotating to the next calibration angle failed.");
                }

                flatItem.ResetProgress();
                await flatItem.Run(progress, token);
                if (flatItem.Status == SequenceEntityStatus.FAILED) {
                    throw new InvalidOperationException("NightFront: capturing calibration flats failed.");
                }

                NightFrontMetadataStore.MarkCompleted(livePath, claimed.Id);
            } catch {
                NightFrontMetadataStore.ReleaseClaim(livePath, claimed.Id);
                throw;
            }
        }

        /// <summary>Looks up the live FilterInfo for a filter name against the active profile's
        /// filter wheel (same lookup NightFrontJsonImporter.BuildSwitchFilter uses) and applies it,
        /// plus the claimed gain/offset, onto the wrapped flat instruction's own filter/exposure child
        /// items.</summary>
        protected void ApplyFilterAndExposureSettings(SwitchFilter switchFilterItem, TakeExposure exposureItem, NightFrontCalibrationRequirement claimed) {
            var liveFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters
                .FirstOrDefault(f => string.Equals(f.Name, claimed.Filter, StringComparison.OrdinalIgnoreCase));
            if (liveFilter == null) {
                throw new InvalidOperationException($"NightFront calibration metadata references filter '{claimed.Filter}', which is not present in the currently configured filter wheel.");
            }

            switchFilterItem.Filter = liveFilter;
            exposureItem.Gain = claimed.Gain;
            exposureItem.Offset = claimed.Offset;
        }

        protected abstract void ConfigureFlatItem(NightFrontCalibrationRequirement claimed);
    }
}
