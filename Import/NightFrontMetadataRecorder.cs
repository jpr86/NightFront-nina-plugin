using NINA.Core.Enum;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Platesolving;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Watches the imported plan's live CenterAndRotate instructions as they execute and records each
    /// target's actual measured rotator position - read from IRotatorMediator right as CenterAndRotate
    /// finishes, not the plan's input Sky PA - alongside the (filter, gain, offset) combinations
    /// planned for that target, via NightFrontMetadataStore. Holds no in-memory copy of the metadata
    /// file's contents itself: every read/write goes through the store, since the file now
    /// accumulates across nights and is also touched by the calibration-consuming flat instructions
    /// during the same running sequence - a private snapshot here would go stale the moment anything
    /// else modified the file. Nothing is written to the store until a target's CenterAndRotate
    /// actually finishes - constructing this class only walks the imported tree in memory and
    /// subscribes PropertyChanged handlers, so a plan that's imported but never run leaves the
    /// metadata file untouched. Assumes at most one CenterAndRotate per target, matching what
    /// NightFrontJsonImporter's supported plan shape and NightFrontApp's exporter both produce today.
    /// </summary>
    public class NightFrontMetadataRecorder {
        private readonly IRotatorMediator rotatorMediator;
        private readonly string sourcePlanFileName;
        private readonly string livePath;
        private readonly string archivedPath;

        public NightFrontMetadataRecorder(IEnumerable<ISequenceItem> importedTopLevelItems, IRotatorMediator rotatorMediator, string sourcePlanFileName, string livePath, string archivedPath) {
            this.rotatorMediator = rotatorMediator;
            this.sourcePlanFileName = sourcePlanFileName;
            this.livePath = livePath;
            this.archivedPath = archivedPath;

            foreach (var item in importedTopLevelItems) {
                AttachTargets(item);
            }
        }

        private void AttachTargets(ISequenceItem item) {
            if (item is DeepSkyObjectContainer dso) {
                AttachTarget(dso);
            }

            if (item is ISequenceContainer container) {
                foreach (var child in container.Items) {
                    AttachTargets(child);
                }
            }
        }

        private void AttachTarget(DeepSkyObjectContainer dso) {
            var name = string.IsNullOrWhiteSpace(dso.Target?.TargetName) ? dso.Name : dso.Target.TargetName;

            var exposures = new List<(string Filter, int Gain, int Offset)>();
            string currentFilter = null;
            CollectFilterExposures(dso, exposures, ref currentFilter, name);

            var centerAndRotate = FindCenterAndRotate(dso);
            if (centerAndRotate == null) {
                return;
            }

            PropertyChangedEventHandler handler = null;
            handler = (sender, e) => {
                if (centerAndRotate.Status != SequenceEntityStatus.FINISHED) {
                    return;
                }

                centerAndRotate.PropertyChanged -= handler;
                RecordMeasuredRotation(name, exposures);
            };
            centerAndRotate.PropertyChanged += handler;
        }

        private void RecordMeasuredRotation(string targetName, List<(string Filter, int Gain, int Offset)> exposures) {
            double measuredAngle;
            try {
                measuredAngle = rotatorMediator.GetInfo().MechanicalPosition;
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: could not read the rotator's measured position for '{targetName}': {ex.Message}");
                return;
            }

            // Deferred here (rather than done once up front for every target at construction time)
            // so nothing is written to the metadata file until a target's rotation is actually
            // measured - see this class's own doc comment. Harmless to repeat once per target as
            // each one's CenterAndRotate finishes: it just re-stamps "most recently touched by".
            NightFrontMetadataStore.RecordRunStarted(livePath, sourcePlanFileName);

            var filters = exposures.Select(e => e.Filter).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            NightFrontMetadataStore.UpsertTargetMetadata(livePath, targetName, filters);
            NightFrontMetadataStore.RecordMeasuredRotationAngle(livePath, targetName, measuredAngle);

            foreach (var exposure in exposures) {
                NightFrontMetadataStore.TryAddCalibrationRequirement(livePath, archivedPath, exposure.Filter, measuredAngle, exposure.Gain, exposure.Offset);
            }
        }

        /// <summary>
        /// Walks a target's imported instructions in document order, pairing each TakeExposure with
        /// the most-recently-seen SwitchFilter's filter name - so gain/offset are correctly attributed
        /// even when a target has more than one filter/exposure block. A TakeExposure with no
        /// preceding SwitchFilter anywhere earlier in the target's subtree can't be attributed to any
        /// filter, so it's skipped - surfaced via a warning rather than silently, since it means that
        /// exposure's calibration requirement will never be recorded.
        /// </summary>
        private static void CollectFilterExposures(ISequenceContainer container, List<(string Filter, int Gain, int Offset)> results, ref string currentFilter, string targetName) {
            foreach (var item in container.Items) {
                if (item is SwitchFilter switchFilter && !string.IsNullOrEmpty(switchFilter.Filter?.Name)) {
                    currentFilter = switchFilter.Filter.Name;
                } else if (item is TakeExposure takeExposure) {
                    if (currentFilter == null) {
                        Notification.ShowWarning($"NightFront: '{targetName}' has an exposure with no preceding filter switch - it will not be included in calibration metadata.");
                    } else {
                        var combo = (currentFilter, takeExposure.Gain, takeExposure.Offset);
                        if (!results.Contains(combo)) {
                            results.Add(combo);
                        }
                    }
                } else if (item is ISequenceContainer nested) {
                    CollectFilterExposures(nested, results, ref currentFilter, targetName);
                }
            }
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
