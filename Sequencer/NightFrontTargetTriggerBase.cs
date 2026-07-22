using NINA.Core.Model;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Shared base for the "Before Target" and "After Target" triggers. Both fire around a target's
    /// own DeepSkyObjectContainer as NightFront's imported plan executes inside the NightFront
    /// Container, running whatever
    /// instructions the user drops into TriggerRunner - the same generic condition-to-action bridge
    /// NightFrontSeeingTrigger already uses, minus its background poller: these read
    /// ShouldTrigger/ShouldTriggerAfter's own previousItem/nextItem arguments directly instead of a
    /// sampled condition.
    ///
    /// The trick that makes Ground Station's own $$TARGET_NAME$$/$$TARGET_RA$$/$$TARGET_DEC$$ tokens
    /// resolve inside the dropped instructions: daleghent/nina-ground-station's
    /// Utilities.FindDsoInfo walks UP the parent chain only, looking for the nearest
    /// IDeepSkyObjectContainer ancestor. TriggerRunner's own parent is never set by NINA itself
    /// (SequenceContainer.Add(ISequenceTrigger) only attaches the trigger, not its TriggerRunner), so
    /// by convention a dropped instruction's parent chain would otherwise never reach a
    /// DeepSkyObjectContainer at all. Execute (below) re-parents TriggerRunner onto the target's own
    /// DeepSkyObjectContainer for the duration of the run, then restores the original parent in a
    /// finally - never leaving TriggerRunner pointed at an imported container that a replan (or the
    /// next Nightly Update) can replace wholesale out from under it, and never risking that dangling
    /// reference getting serialized into a saved sequence (TriggerRunner is [JsonProperty]).
    /// </summary>
    public abstract class NightFrontTargetTriggerBase : SequenceTrigger, IValidatable {

        protected NightFrontTargetTriggerBase(string triggerRunnerName) {
            TriggerRunner = new SequentialContainer { Name = triggerRunnerName };
        }

        /// <summary>Shared clone-the-runner-items idiom (mirrors NightFrontSeeingTrigger's own clone
        /// ctor) - called by each derived clone ctor after `this()` has already given it a fresh,
        /// empty TriggerRunner. Deliberately does not carry over CurrentTarget/TargetName/
        /// LastFiredTimestamp/StatusMessage - a clone starts fresh, exactly like NightFrontSeeingTrigger's
        /// clone leaves its own live-poller state behind.</summary>
        protected void CopyRunnerItems(NightFrontTargetTriggerBase copyMe) {
            CopyMetaData(copyMe);
            foreach (var item in copyMe.TriggerRunner.Items) {
                TriggerRunner.Add((ISequenceItem)item.Clone());
            }
        }

        /// <summary>
        /// True only for a target's own DeepSkyObjectContainer sitting directly inside a
        /// NightFrontContainer. The `dso.Parent is NightFrontContainer` clause is load-bearing, not
        /// defensive: both ShouldTrigger and ShouldTriggerAfter propagate up the parent chain
        /// (SequentialStrategy.RunTriggers/RunTriggersAfter), so a trigger placed on an ancestor
        /// (e.g. "Loop while safe") also receives every inner item pair from every nesting level in
        /// the whole sequence tree below it - including unrelated DeepSkyObjectContainers the user's
        /// own template might use outside the NightFront Container. Without this clause the trigger
        /// would fire for those too.
        /// </summary>
        protected static bool IsNightFrontTarget(ISequenceItem item) {
            return item is DeepSkyObjectContainer dso && dso.Parent is NightFrontContainer;
        }

        public IList<string> Issues { get; set; } = new List<string>();

        /// <summary>The target this trigger most recently decided to fire for - captured by the
        /// derived class's ShouldTrigger/ShouldTriggerAfter override via RecordFired the instant it
        /// returns true, and read back by Execute to know which DeepSkyObjectContainer to re-parent
        /// TriggerRunner onto.</summary>
        protected DeepSkyObjectContainer CurrentTarget { get; private set; }

        private string targetName;

        /// <summary>Read-only in the sequencer UI - the name of the target this trigger most recently
        /// fired for.</summary>
        public string TargetName {
            get => targetName;
            private set {
                targetName = value;
                RaisePropertyChangedOnUIThread(nameof(TargetName));
            }
        }

        private DateTime? lastFiredTimestamp;

        /// <summary>Read-only in the sequencer UI - UTC timestamp of the most recent fire.</summary>
        public DateTime? LastFiredTimestamp {
            get => lastFiredTimestamp;
            private set {
                lastFiredTimestamp = value;
                RaisePropertyChangedOnUIThread(nameof(LastFiredTimestamp));
            }
        }

        private string statusMessage = "Not yet fired";

        /// <summary>Read-only in the sequencer UI.</summary>
        public string StatusMessage {
            get => statusMessage;
            private set {
                statusMessage = value;
                RaisePropertyChangedOnUIThread(nameof(StatusMessage));
            }
        }

        /// <summary>Same pattern as NightFrontSeeingTrigger/NightFrontUpdateInstruction/
        /// NightFrontReplanInstruction: these properties are bound in the sequencer UI, which WPF
        /// requires only be touched from the Dispatcher thread, but ShouldTrigger/ShouldTriggerAfter
        /// run off the UI thread as part of NINA's own sequence engine.</summary>
        private void RaisePropertyChangedOnUIThread(string propertyName) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) {
                dispatcher.Invoke(() => RaisePropertyChanged(propertyName));
            } else {
                RaisePropertyChanged(propertyName);
            }
        }

        /// <summary>Records dso as the target this trigger just decided to fire for. Called by the
        /// derived class's ShouldTrigger/ShouldTriggerAfter override only on the path that returns
        /// true - never directly by Execute.</summary>
        protected void RecordFired(DeepSkyObjectContainer dso) {
            CurrentTarget = dso;
            TargetName = dso?.Target?.TargetName ?? dso?.Name;
            LastFiredTimestamp = DateTime.UtcNow;
            StatusMessage = $"Fired for {TargetName ?? "(unknown target)"} at {LastFiredTimestamp:t}";
        }

        public override void AfterParentChanged() {
            TriggerRunner?.AttachNewParent(Parent);
        }

        /// <summary>
        /// Re-parents TriggerRunner onto CurrentTarget for the duration of the run - so Ground
        /// Station's $$TARGET_NAME$$/$$TARGET_RA$$/$$TARGET_DEC$$ (and Sequencer+ {TargetName}
        /// expressions) resolve as if the dropped instructions lived directly inside the target's own
        /// DeepSkyObjectContainer - then restores the original parent in a finally, so a leaked
        /// reference can never outlive its target or end up serialized into a saved sequence
        /// (imported containers are replaced wholesale on every PopulateItems/replan).
        /// </summary>
        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var dso = CurrentTarget;
            var original = TriggerRunner.Parent;
            try {
                if (dso != null) {
                    TriggerRunner.AttachNewParent(dso);
                }
                await TriggerRunner.Run(progress, token);
            } finally {
                TriggerRunner.AttachNewParent(original);
            }
        }

        /// <summary>
        /// Warns when no NightFrontContainer is reachable at all (NightFrontContainer.FindAnywhere),
        /// and also when one IS reachable but this trigger isn't actually an ancestor of it - the
        /// "Once Safe" misplacement: FindAnywhere searches the WHOLE tree, so it would happily find
        /// the real NightFrontContainer sitting in a sibling branch like "Loop while safe" even
        /// though this trigger, placed in "Once Safe", never sees that container's targets execute
        /// (RunTriggers/RunTriggersAfter only walk UP from the executing container's own parent
        /// chain). A plain "exists somewhere in the tree" check would silently pass that mistake.
        /// </summary>
        public virtual bool Validate() {
            var issues = new List<string>();

            // NightFrontContainer.FindAnywhere takes an ISequenceItem, which a trigger (ISequenceTrigger)
            // isn't - triggers attach via a container's Triggers collection, not its Items collection.
            // FindNightFrontContainerAnywhere below mirrors FindAnywhere's own logic (walk up to the
            // ultimate root, then search every descendant depth-first) but starts from this trigger's
            // own Parent directly, which IS an ISequenceContainer.
            var container = FindNightFrontContainerAnywhere(Parent);
            if (container == null) {
                issues.Add("No NightFront Container found anywhere in this sequence - this trigger only fires for targets inside one.");
            } else if (!IsAncestorOfOrSame(Parent, container)) {
                issues.Add("This trigger's placement can never see the NightFront Container's targets - it must sit on the NightFront Container itself, or one of its ancestors (e.g. the same branch as 'Loop while safe'), not a sibling branch such as 'Once Safe'.");
            }

            TriggerRunner?.Validate();
            if (TriggerRunner != null) {
                foreach (var issue in TriggerRunner.Issues) {
                    issues.Add(issue);
                }
            }

            Issues = issues;
            return issues.Count == 0;
        }

        /// <summary>True when ancestor IS candidate, or is found by walking candidate's own Parent
        /// chain upward - i.e. "ancestor is candidate or an ancestor of candidate."</summary>
        private static bool IsAncestorOfOrSame(ISequenceContainer ancestor, ISequenceContainer candidate) {
            var current = candidate;
            while (current != null) {
                if (ReferenceEquals(current, ancestor)) {
                    return true;
                }
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// Mirrors NightFrontContainer.FindAnywhere's own logic (walk up to the ultimate root
        /// container, then search every descendant depth-first) but starts from an
        /// <see cref="ISequenceContainer"/> directly instead of an <see cref="ISequenceItem"/> -
        /// FindAnywhere can't be called on a trigger itself since ISequenceTrigger isn't an
        /// ISequenceItem (triggers attach via a container's Triggers collection, not its Items
        /// collection).
        /// </summary>
        private static NightFrontContainer FindNightFrontContainerAnywhere(ISequenceContainer startParent) {
            if (startParent == null) {
                return null;
            }

            var root = startParent;
            while (root.Parent != null) {
                root = root.Parent;
            }

            var visited = new HashSet<ISequenceContainer>(ReferenceEqualityComparer.Instance);
            return FindInSubtree(root, visited);
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
    }
}
