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
    /// Holds the instructions imported from a NightFront plan file. Populated at runtime by an
    /// earlier Nightly Update instruction in the same sequence branch - either a preceding sibling
    /// of this container, or nested inside one of that sibling's descendant containers (e.g. a
    /// nightly imaging loop); behaves as a normal SequentialContainer once populated.
    /// </summary>
    [ExportMetadata("Name", "NightFront Container")]
    [ExportMetadata("Description", "Holds the imaging instructions imported from a NightFront plan for the night. Place a Nightly Update instruction earlier in the sequence (as a preceding sibling of this container, or an ancestor of one) to populate it.")]
    [ExportMetadata("Icon", "NightFront_SVG")]
    [ExportMetadata("Category", "NightFront")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
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
        /// Finds the first NightFrontContainer reachable by scanning forward through
        /// <paramref name="siblings"/> after <paramref name="after"/>, descending into each
        /// following sibling's own subtree (depth-first, in execution order) if it doesn't match
        /// directly. Used by NightFrontUpdateInstruction to locate the container it should
        /// populate - it need not be an immediate sibling; it may be nested inside a later
        /// sibling's descendant containers (e.g. a nightly imaging loop). Guards against a cyclic
        /// container graph (e.g. from a corrupted saved sequence) revisiting the same container.
        /// </summary>
        public static NightFrontContainer FindNext(IList<ISequenceItem> siblings, ISequenceItem after) {
            if (siblings == null) {
                return null;
            }

            var idx = siblings.IndexOf(after);
            if (idx < 0) {
                return null;
            }

            var visited = new HashSet<ISequenceContainer>(ReferenceEqualityComparer.Instance);
            for (int i = idx + 1; i < siblings.Count; i++) {
                var found = FindInSubtree(siblings[i], visited);
                if (found != null) {
                    return found;
                }
            }

            return null;
        }

        private static NightFrontContainer FindInSubtree(ISequenceItem item, HashSet<ISequenceContainer> visited) {
            if (item is NightFrontContainer container) {
                return container;
            }
            if (item is ISequenceContainer parentContainer && visited.Add(parentContainer)) {
                foreach (var child in parentContainer.Items) {
                    var found = FindInSubtree(child, visited);
                    if (found != null) {
                        return found;
                    }
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
