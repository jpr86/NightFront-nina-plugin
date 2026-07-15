using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Shared tree-walking helpers over a freshly-imported plan's top-level items, used by every
    /// class that needs to find each target's DeepSkyObjectContainer/CenterAndRotate - currently
    /// NightFrontMetadataRecorder (Import/) and NightFrontCenterAfterDriftCoordinator (Sequencer/),
    /// both of which independently re-derived the identical walk before this was factored out.
    /// Internal, not private to either class, since both live in the same assembly but different
    /// namespaces.
    /// </summary>
    internal static class NightFrontSequenceTreeWalker {

        /// <summary>Invokes <paramref name="onTarget"/> once for every DeepSkyObjectContainer found
        /// anywhere in <paramref name="items"/>'s subtree (depth-first, document order).</summary>
        public static void ForEachDeepSkyObjectContainer(IEnumerable<ISequenceItem> items, Action<DeepSkyObjectContainer> onTarget) {
            foreach (var item in items) {
                if (item is DeepSkyObjectContainer dso) {
                    onTarget(dso);
                }
                if (item is ISequenceContainer container) {
                    ForEachDeepSkyObjectContainer(container.Items, onTarget);
                }
            }
        }

        /// <summary>Finds the first CenterAndRotate anywhere in <paramref name="container"/>'s
        /// subtree (depth-first, document order), or null if none exists.</summary>
        public static CenterAndRotate FindCenterAndRotate(ISequenceContainer container) {
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
