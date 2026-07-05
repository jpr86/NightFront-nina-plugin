using NINA.Core.Enum;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using Newtonsoft.Json;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Platesolving;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Watches the imported plan's live CenterAndRotate instructions as they execute and records
    /// each target's actual measured rotator position - read from IRotatorMediator right as
    /// CenterAndRotate finishes, not the plan's input Sky PA - alongside the filters planned for
    /// that target, writing the result to a JSON side-file for a future calibration-frame
    /// instruction to consume. Assumes at most one CenterAndRotate per target, matching what
    /// NightFrontJsonImporter's supported plan shape and NightFrontApp's exporter both produce
    /// today.
    /// </summary>
    public class NightFrontMetadataRecorder {
        private const double DuplicateAngleToleranceDegrees = 1.0;

        private readonly IRotatorMediator rotatorMediator;
        private readonly string metadataPath;
        private readonly NightFrontPlanMetadata metadata;
        private readonly object sync = new object();

        public NightFrontMetadataRecorder(IEnumerable<ISequenceItem> importedTopLevelItems, IRotatorMediator rotatorMediator, string sourcePlanFileName, string metadataPath) {
            this.rotatorMediator = rotatorMediator;
            this.metadataPath = metadataPath;
            metadata = new NightFrontPlanMetadata {
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
                SourcePlanFile = sourcePlanFileName,
                GeneratedAtUtc = DateTime.UtcNow
            };

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
            var filters = new List<string>();
            CollectFilters(dso, filters);

            var targetMeta = new NightFrontTargetMetadata { TargetName = name, Filters = filters };
            lock (sync) {
                metadata.Targets.Add(targetMeta);
            }

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
                RecordMeasuredRotation(targetMeta, filters);
            };
            centerAndRotate.PropertyChanged += handler;
        }

        private void RecordMeasuredRotation(NightFrontTargetMetadata targetMeta, List<string> filters) {
            double measuredAngle;
            try {
                measuredAngle = rotatorMediator.GetInfo().MechanicalPosition;
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: could not read the rotator's measured position for '{targetMeta.TargetName}': {ex.Message}");
                return;
            }

            lock (sync) {
                targetMeta.MeasuredRotationAngle = measuredAngle;

                foreach (var filter in filters) {
                    AddCalibrationRequirement(filter, measuredAngle);
                }

                WriteToFile();
            }
        }

        private void AddCalibrationRequirement(string filter, double angle) {
            var alreadyCovered = metadata.CalibrationRequirements.Any(r =>
                string.Equals(r.Filter, filter, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(r.RotationAngle - angle) < DuplicateAngleToleranceDegrees);
            if (!alreadyCovered) {
                metadata.CalibrationRequirements.Add(new NightFrontCalibrationRequirement { Filter = filter, RotationAngle = angle });
            }
        }

        private void WriteToFile() {
            try {
                File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: failed to write calibration metadata file: {ex.Message}");
            }
        }

        private static void CollectFilters(ISequenceContainer container, List<string> filters) {
            foreach (var item in container.Items) {
                if (item is SwitchFilter switchFilter && !string.IsNullOrEmpty(switchFilter.Filter?.Name)) {
                    if (!filters.Contains(switchFilter.Filter.Name)) {
                        filters.Add(switchFilter.Filter.Name);
                    }
                } else if (item is ISequenceContainer nested) {
                    CollectFilters(nested, filters);
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
