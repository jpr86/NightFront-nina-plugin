using NINA.Core.Enum;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.Platesolving;
using System.Collections.Generic;
using System.ComponentModel;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Keeps a CenterAfterDriftTrigger placed OUTSIDE the NightFront Container (a sibling/ancestor
    /// branch of the user's own sequence template - the maintainer's own production template places
    /// it on "Loop while safe", not nested inside the imported plan) pointed at whichever target is
    /// actually executing.
    ///
    /// NINA's own CenterAfterDriftTrigger resolves its Coordinates exactly once, in
    /// AfterParentChanged(), by walking upward from its own static Parent looking for the nearest
    /// IDeepSkyObjectContainer ancestor (NINA.Sequencer.Utility.ItemUtility.RetrieveContextCoordinates).
    /// Every DeepSkyObjectContainer NightFront imports lives BELOW the NightFront Container -
    /// reachable only by walking down from whatever's currently executing, never by walking up from a
    /// trigger declared outside it - so that lookup finds nothing (Inherited stays false) and the
    /// trigger centers on its constructor default (RA 0, Dec 0) for the entire night. Confirmed live:
    /// NINA slewed to RA 00h01m/Dec 00d09m - not the actual target - the moment the trigger fired.
    ///
    /// tcpalmer/nina.plugin.targetscheduler solves the identical problem for its own container
    /// (TargetSchedulerContainer.ResetCenterAfterDrift/GetCenterAfterDriftTrigger): rather than rely
    /// on NINA's inheritance, it actively walks up from its own Parent every time the active target
    /// changes and pushes fresh Coordinates/Inherited directly into any CenterAfterDriftTrigger it
    /// finds in an ancestor's Triggers collection, then calls SequenceBlockInitialize() to reset its
    /// internal PlatesolvingImageFollower. That plugin can do this from a natural "target is about to
    /// change" hook because it drives its own execution loop one target at a time. NightFront instead
    /// hands the whole night's pre-built tree off to NINA's native engine in one shot (see
    /// NightFrontContainer.PopulateItems), so there's no equivalent hook to push from directly -
    /// this class creates one by watching each imported target's own CenterAndRotate the same way
    /// NightFrontMetadataRecorder already watches TakeExposure completion, and pushes the moment NINA
    /// actually starts running it (Status -> RUNNING, i.e. right as NINA commits to slewing/centering
    /// on that target - well before any exposure, let alone a drift check, could occur).
    /// </summary>
    public class NightFrontCenterAfterDriftCoordinator {
        private readonly NightFrontContainer container;

        public NightFrontCenterAfterDriftCoordinator(NightFrontContainer container, IEnumerable<ISequenceItem> importedTopLevelItems) {
            this.container = container;
            foreach (var item in importedTopLevelItems) {
                AttachTargets(item);
            }
        }

        private void AttachTargets(ISequenceItem item) {
            if (item is DeepSkyObjectContainer dso) {
                AttachTarget(dso);
            }

            if (item is ISequenceContainer nested) {
                foreach (var child in nested.Items) {
                    AttachTargets(child);
                }
            }
        }

        private void AttachTarget(DeepSkyObjectContainer dso) {
            var centerAndRotate = FindCenterAndRotate(dso);
            if (centerAndRotate == null) {
                return;
            }

            PropertyChangedEventHandler handler = null;
            handler = (sender, e) => {
                if (e.PropertyName != nameof(centerAndRotate.Status) || centerAndRotate.Status != SequenceEntityStatus.RUNNING) {
                    return;
                }
                centerAndRotate.PropertyChanged -= handler;
                PushCoordinates(dso);
            };
            centerAndRotate.PropertyChanged += handler;
        }

        private void PushCoordinates(DeepSkyObjectContainer dso) {
            var trigger = FindCenterAfterDriftTrigger();
            if (trigger == null || dso.Target?.InputCoordinates == null) {
                return;
            }

            trigger.Coordinates = dso.Target.InputCoordinates.Clone();
            trigger.Inherited = true;
            trigger.SequenceBlockInitialize();
        }

        /// <summary>
        /// Mirrors TargetSchedulerContainer.GetCenterAfterDriftTrigger: walks upward from this
        /// container's own Parent (not the trigger's) - the exact opposite direction of NINA's
        /// built-in RetrieveContextCoordinates - since the trigger lives in an ANCESTOR branch of the
        /// NightFront Container, not a descendant of it.
        /// </summary>
        private CenterAfterDriftTrigger FindCenterAfterDriftTrigger() {
            var ancestor = container.Parent;
            while (ancestor != null) {
                if (ancestor is ITriggerable triggerable) {
                    foreach (var trigger in triggerable.GetTriggersSnapshot()) {
                        if (trigger is CenterAfterDriftTrigger centerAfterDriftTrigger) {
                            return centerAfterDriftTrigger;
                        }
                    }
                }
                ancestor = ancestor.Parent;
            }
            return null;
        }

        private static CenterAndRotate FindCenterAndRotate(ISequenceContainer container) {
            foreach (var item in container.Items) {
                if (item is CenterAndRotate centerAndRotate) {
                    return centerAndRotate;
                }
                if (item is ISequenceContainer nested) {
                    var found = FindCenterAndRotate(nested);
                    if (found != null) {
                        return found;
                    }
                }
            }
            return null;
        }
    }
}
