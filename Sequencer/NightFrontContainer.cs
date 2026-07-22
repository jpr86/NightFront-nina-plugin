using JeffRidder.NINA.Nightfront.Import;
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
        /// The file name (not full path) of the plan file this container's current Items were last
        /// populated from - the authoritative "what plan is this, really" signal for
        /// NightFrontReplanInstruction. NightFrontApp routinely exports several nights' plan files at
        /// once (e.g. "TargetsForTonight_2026-07-14.json" through "...-07-16.json" sitting in the data
        /// folder simultaneously), so re-deriving "the current plan" by scanning the folder for a
        /// date-matching (or even just most-recently-written) file is unsound - after local midnight,
        /// a date-based scan can just as easily match a *later* night's already-exported file as the
        /// one actually loaded, silently replanning the wrong night. Remembering the exact source
        /// filename at population time sidesteps that ambiguity entirely. Null until PopulateItems is
        /// first called with a non-null name (e.g. a container freshly dragged into the editor, never
        /// yet populated). [JsonProperty] is required, not decorative: this class is
        /// [JsonObject(MemberSerialization.OptIn)], and the base SequenceContainer's Items already
        /// survives NINA's own sequence save/reload that way - without this attribute, reloading a
        /// saved sequence would bring Items back intact but silently reset this to null, reopening the
        /// exact after-midnight ambiguity above the moment a Replan next ran against the reloaded
        /// container.
        /// </summary>
        [JsonProperty]
        public string SourcePlanFileName { get; private set; }

        /// <summary>
        /// Replaces the container's current children with <paramref name="newItems"/>. Called by
        /// NightFrontUpdateInstruction (the nightly import) and NightFrontReplanInstruction (a
        /// mid-night replan) once either has successfully imported a plan file. Each Remove/Add below
        /// triggers a TargetSummaries rebuild via the CollectionChanged subscription set up in the
        /// constructor - the same mechanism that keeps TargetSummaries in sync with manual
        /// drag-reorder/delete done directly in the sequencer editor. <paramref
        /// name="sourcePlanFileName"/> updates <see cref="SourcePlanFileName"/> when given; passing
        /// null leaves it unchanged - used by NightFrontReplanInstruction's "every target already
        /// complete" path, which empties the container without any new file actually replacing the
        /// one on disk, so the remembered name shouldn't change either.
        /// </summary>
        public void PopulateItems(IEnumerable<ISequenceItem> newItems, string sourcePlanFileName = null) {
            foreach (var existing in Items.ToList()) {
                Remove(existing);
            }
            foreach (var item in newItems) {
                Add(item);
            }
            if (sourcePlanFileName != null) {
                SourcePlanFileName = sourcePlanFileName;
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
        /// Builds a point-in-time snapshot of PlannedCount/CompletedCount/Status for every target row
        /// in TargetSummaries (NightFrontApp's docs/DESIGN.md § Safety-Recovery Replan, Finding 7 /
        /// Phase 1) - the already-live progress data a future safety-recovery replan step (Phase 3)
        /// can serialize alongside a live weather reading and hand to a NightFront CLI
        /// remainder-of-night re-solve.
        /// Reads TargetSummaries on the UI thread, same as RebuildTargetSummaries - the intended
        /// caller (a future Phase 3 sequence instruction's Execute) runs off the UI thread like any
        /// other NINA sequence item, and TargetSummaries is the same WPF-bound ObservableCollection
        /// RebuildTargetSummariesOnCurrentThread mutates in place (Clear then re-Add), so an
        /// unmarshaled read here could race a concurrent rebuild triggered by a manual sequencer edit.
        /// A TargetSummaries entry that isn't a NightFrontTargetSummary - a raw top-level ISequenceItem,
        /// or a target whose summary row failed to build (see RebuildTargetSummariesOnCurrentThread) -
        /// still gets a row with null PlannedCount/CompletedCount rather than being omitted: a future
        /// remainder-of-night re-solve needs to tell "this target has unknown progress" apart from
        /// "this target was never part of tonight's plan," since the two would otherwise both simply
        /// be absent from Targets.
        /// </summary>
        public NightFrontProgressSnapshot BuildProgressSnapshot() {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                return dispatcher.Invoke(BuildProgressSnapshotOnCurrentThread);
            }
            return BuildProgressSnapshotOnCurrentThread();
        }

        private NightFrontProgressSnapshot BuildProgressSnapshotOnCurrentThread() {
            var snapshot = new NightFrontProgressSnapshot();
            foreach (var summary in TargetSummaries) {
                switch (summary) {
                    case NightFrontTargetSummary targetSummary:
                        snapshot.Targets.Add(new NightFrontTargetProgress {
                            Name = targetSummary.Name,
                            PlannedCount = targetSummary.PlannedCount,
                            CompletedCount = targetSummary.CompletedCount,
                            Status = targetSummary.Status.ToString(),
                        });
                        break;
                    case ISequenceItem item:
                        snapshot.Targets.Add(new NightFrontTargetProgress {
                            Name = item.Name,
                            Status = item.Status.ToString(),
                        });
                        break;
                }
            }
            return snapshot;
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

        /// <summary>
        /// Finds a NightFrontContainer anywhere in the whole sequence tree containing
        /// <paramref name="from"/>, by walking up to the ultimate root container and then searching
        /// every descendant depth-first. Used by NightFrontReplanInstruction (todos/
        /// nina-safety-delay-plan.md, Phase 3), which - unlike NightFrontUpdateInstruction - has no
        /// fixed "runs before, in a later sibling" relationship to the container it needs to reach:
        /// the maintainer's own production template places it inside "Once Safe," a sibling branch
        /// of "Loop while safe" (where the real NightFrontContainer lives), not a preceding sibling
        /// of it. FindNext's forward-from-a-specific-sibling search can only ever look inside the
        /// subtree of siblings that come after a given item in one specific list, so it can't reach
        /// across into a completely different branch of the tree the way this needs to - hence a
        /// full search from the top instead of a positional one. Returns null if
        /// <paramref name="from"/> has no parent (e.g. it IS the root, or isn't attached to a
        /// sequence yet) or no NightFrontContainer exists anywhere in the tree.
        /// </summary>
        public static NightFrontContainer FindAnywhere(ISequenceItem from) {
            ISequenceContainer root = from?.Parent;
            if (root == null) {
                return null;
            }
            while (root.Parent != null) {
                root = root.Parent;
            }

            var visited = new HashSet<ISequenceContainer>(ReferenceEqualityComparer.Instance);
            return FindInSubtree(root, visited);
        }

        public override object Clone() {
            var clone = new NightFrontContainer(this) {
                SourcePlanFileName = SourcePlanFileName
            };
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
