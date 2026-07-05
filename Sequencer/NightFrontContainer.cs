using Newtonsoft.Json;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Holds the instructions imported from a NightFront plan file. Populated at runtime by a
    /// preceding Nightly Update instruction placed immediately before this container in the same
    /// parent; behaves as a normal SequentialContainer once populated.
    /// </summary>
    [ExportMetadata("Name", "NightFront Container")]
    [ExportMetadata("Description", "Holds the imaging instructions imported from a NightFront plan for the night. Place a Nightly Update instruction immediately before this container (as its preceding sibling) to populate it.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class NightFrontContainer : SequentialContainer {

        [ImportingConstructor]
        public NightFrontContainer() : base() {
            Name = "NightFront Container";

            // Items is a real ObservableCollection under the hood, so this also catches manual
            // drag-reorder/delete done directly in NINA's Advanced Sequencer editor - not just the
            // Remove/Add calls PopulateItems itself makes.
            if (Items is INotifyCollectionChanged notifyingItems) {
                notifyingItems.CollectionChanged += (sender, e) => RebuildTargetSummaries();
            }
        }

        private NightFrontContainer(NightFrontContainer copyMe) : this() {
            CopyMetaData(copyMe);
        }

        /// <summary>
        /// Per-target summary rows for the sequencer UI, rebuilt whenever Items changes (via the
        /// CollectionChanged subscription set up in the constructor, so this covers PopulateItems,
        /// Clone, and manual edits in the sequencer editor alike). Holds a
        /// <see cref="NightFrontTargetSummary"/> for each top-level DeepSkyObjectContainer target, or
        /// the raw <see cref="ISequenceItem"/> itself for any other top-level item - including a
        /// DeepSkyObjectContainer whose summary failed to build - rendered via a generic fallback row
        /// (see NightFrontTargetRowTemplateSelector).
        /// </summary>
        public ObservableCollection<object> TargetSummaries { get; } = new ObservableCollection<object>();

        /// <summary>
        /// Replaces the container's current children with <paramref name="newItems"/>. Called by
        /// NightFrontUpdateInstruction once it has successfully imported a plan file. Each Remove/Add
        /// below triggers a TargetSummaries rebuild via the CollectionChanged subscription set up in
        /// the constructor - the same mechanism that keeps TargetSummaries in sync with manual
        /// drag-reorder/delete done directly in the sequencer editor.
        /// </summary>
        public void PopulateItems(IEnumerable<ISequenceItem> newItems) {
            foreach (var existing in Items.ToList()) {
                Remove(existing);
            }
            foreach (var item in newItems) {
                Add(item);
            }
        }

        /// <summary>
        /// Rebuilds TargetSummaries from the current Items. Runs on the UI thread even when called
        /// from a background thread (NINA executes sequence items off the UI thread, and
        /// TargetSummaries is bound as an ItemsControl.ItemsSource, which WPF requires to only be
        /// mutated from the Dispatcher thread).
        /// </summary>
        private void RebuildTargetSummaries() {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.Invoke(RebuildTargetSummariesOnCurrentThread);
            } else {
                RebuildTargetSummariesOnCurrentThread();
            }
        }

        private void RebuildTargetSummariesOnCurrentThread() {
            foreach (var old in TargetSummaries.OfType<NightFrontTargetSummary>()) {
                old.Dispose();
            }

            TargetSummaries.Clear();

            NightFrontTargetSummary previous = null;
            foreach (var item in Items) {
                if (item is DeepSkyObjectContainer dso) {
                    try {
                        var summary = new NightFrontTargetSummary(dso) {
                            WindowStart = previous?.WindowEnd
                        };
                        TargetSummaries.Add(summary);
                        previous = summary;
                    } catch (Exception ex) {
                        // A summary row is supplementary UI - a failure building one must not
                        // prevent the plan from having been imported successfully. Fall back to a
                        // generic row for this target rather than losing the whole summary.
                        Notification.ShowWarning($"NightFront: could not build a summary row for '{dso.Name}': {ex.Message}");
                        TargetSummaries.Add(item);
                        previous = null;
                    }
                } else {
                    TargetSummaries.Add(item);
                }
            }
        }

        /// <summary>
        /// Finds the first NightFrontContainer in <paramref name="siblings"/> that comes after
        /// <paramref name="after"/>. Used by NightFrontUpdateInstruction to locate the container it
        /// should populate, now that it runs as a preceding sibling rather than a child.
        /// </summary>
        public static NightFrontContainer FindNext(IList<ISequenceItem> siblings, ISequenceItem after) {
            if (siblings == null) {
                return null;
            }

            var idx = siblings.IndexOf(after);
            if (idx < 0) {
                return null;
            }

            for (int i = idx + 1; i < siblings.Count; i++) {
                if (siblings[i] is NightFrontContainer container) {
                    return container;
                }
            }

            return null;
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
