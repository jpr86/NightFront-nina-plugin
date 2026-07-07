using JeffRidder.NINA.Nightfront.Import;
using JeffRidder.NINA.Nightfront.Properties;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Rotates to the sky angle of the next outstanding calibration requirement (the head of the
    /// accumulated calibration-metadata file, rounded to the nearest whole degree), without consuming
    /// it - unlike NightFront Sky Flats/Trained Flats, this instruction only peeks the requirement; it
    /// doesn't claim or archive it.
    /// </summary>
    [ExportMetadata("Name", "NightFront Rotate to Next Angle")]
    [ExportMetadata("Description", "Rotates to the mechanical angle of the next outstanding calibration requirement in the NightFront calibration-metadata file, rounded to the nearest whole degree.")]
    [ExportMetadata("Icon", "RotatorSVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontRotateToNextAngleInstruction : SequenceItem, IValidatable {
        private readonly IRotatorMediator rotatorMediator;

        [ImportingConstructor]
        public NightFrontRotateToNextAngleInstruction(IRotatorMediator rotatorMediator) {
            this.rotatorMediator = rotatorMediator;
        }

        private NightFrontRotateToNextAngleInstruction(NightFrontRotateToNextAngleInstruction copyMe) : this(copyMe.rotatorMediator) {
            CopyMetaData(copyMe);
            BaseName = copyMe.BaseName;
        }

        /// <summary>Which calibration-metadata file to read. Blank auto-detects the single
        /// "*.metadata.json" file in the configured NightFront data folder.</summary>
        [JsonProperty]
        public string BaseName { get; set; } = "";

        public IList<string> Issues { get; set; } = new List<string>();

        public bool Validate() {
            var issues = new List<string>();

            var folder = Settings.Default.NightFrontDataFolder;
            if (string.IsNullOrWhiteSpace(folder)) {
                issues.Add("The NightFront data folder is not configured. Set it on the NightFront plugin's Options tab.");
            } else {
                var resolvedBaseName = NightFrontMetadataPaths.ResolveBaseName(folder, BaseName, out var issue);
                if (resolvedBaseName == null) {
                    issues.Add(issue);
                } else {
                    var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, resolvedBaseName);
                    if (NightFrontMetadataStore.PeekNext(livePath) == null) {
                        issues.Add("No calibration requirements remain in the NightFront calibration-metadata file.");
                    }
                }
            }

            Issues = issues;
            return issues.Count == 0;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var folder = Settings.Default.NightFrontDataFolder;
            var resolvedBaseName = NightFrontMetadataPaths.ResolveBaseName(folder, BaseName, out var issue);
            if (resolvedBaseName == null) {
                throw new InvalidOperationException(issue);
            }

            var livePath = NightFrontMetadataPaths.GetLiveMetadataPath(folder, resolvedBaseName);
            var entry = NightFrontMetadataStore.PeekNext(livePath)
                ?? throw new InvalidOperationException("No calibration requirements remain in the NightFront calibration-metadata file.");

            var angle = (float)Math.Round(entry.RotationAngle, MidpointRounding.AwayFromZero);
            await rotatorMediator.MoveMechanical(angle, token);
        }

        public override object Clone() {
            return new NightFrontRotateToNextAngleInstruction(this);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontRotateToNextAngleInstruction)}, BaseName: {BaseName}";
        }
    }
}
