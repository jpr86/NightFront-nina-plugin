using Newtonsoft.Json;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Holds the instructions imported from a NightFront plan file. Populated at runtime by a nested
    /// NightFrontUpdateInstruction; behaves as a normal SequentialContainer once populated.
    /// </summary>
    [ExportMetadata("Name", "NightFront Container")]
    [ExportMetadata("Description", "Holds the imaging instructions imported from a NightFront plan for the night. Place a NightFront Update instruction inside this container to populate it.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontContainer : SequentialContainer {

        [ImportingConstructor]
        public NightFrontContainer() : base() {
            Name = "NightFront Container";
        }

        private NightFrontContainer(NightFrontContainer copyMe) : this() {
            CopyMetaData(copyMe);
        }

        /// <summary>
        /// Replaces the container's current children with <paramref name="newItems"/>. Called by
        /// NightFrontUpdateInstruction once it has successfully imported a plan file.
        /// </summary>
        public void PopulateItems(IEnumerable<ISequenceItem> newItems) {
            foreach (var existing in Items.ToList()) {
                Remove(existing);
            }
            foreach (var item in newItems) {
                Add(item);
            }
        }

        public override object Clone() {
            var clone = new NightFrontContainer(this);
            foreach (var item in Items) {
                clone.Add((ISequenceItem)item.Clone());
            }
            foreach (var condition in Conditions) {
                clone.Add((ISequenceCondition)condition.Clone());
            }
            foreach (var trigger in Triggers) {
                clone.Add((ISequenceTrigger)trigger.Clone());
            }
            return clone;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(NightFrontContainer)}, Items: {Items.Count}";
        }
    }
}
