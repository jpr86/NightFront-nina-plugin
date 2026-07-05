using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Platesolving;
using System;
using System.Collections.Generic;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Walks the object graph NightFrontJsonImporter already built for a plan and collects the
    /// filters and rotation angles it uses, grouped by target. Operates on the imported
    /// NINA.Sequencer objects rather than the source JSON, and on the whole tree rather than
    /// assuming any particular shape at the top level - NightFrontJsonImporter.BuildItem can put any
    /// supported instruction type there, not just DeepSkyObjectContainer.
    /// </summary>
    public static class NightFrontPlanMetadataExtractor {

        public static NightFrontPlanMetadata Extract(IEnumerable<ISequenceItem> importedTopLevelItems, string sourcePlanFileName) {
            var metadata = new NightFrontPlanMetadata {
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
                SourcePlanFile = sourcePlanFileName,
                GeneratedAtUtc = DateTime.UtcNow
            };

            var targetsByName = new Dictionary<string, NightFrontTargetMetadata>();

            foreach (var item in importedTopLevelItems) {
                Walk(item, null, metadata, targetsByName);
            }

            foreach (var target in metadata.Targets) {
                foreach (var angle in target.RotationAngles) {
                    AddDistinct(metadata.RotationAngles, angle);
                }
                foreach (var filter in target.Filters) {
                    AddDistinctString(metadata.Filters, filter);
                }
            }

            return metadata;
        }

        private static void Walk(ISequenceItem item, NightFrontTargetMetadata currentTarget, NightFrontPlanMetadata metadata, Dictionary<string, NightFrontTargetMetadata> targetsByName) {
            if (item is DeepSkyObjectContainer dso) {
                var name = dso.Target?.TargetName;
                if (string.IsNullOrWhiteSpace(name)) {
                    name = string.IsNullOrWhiteSpace(dso.Name) ? "Ungrouped" : dso.Name;
                }

                currentTarget = GetOrCreateTarget(name, metadata, targetsByName);
                if (dso.Target != null) {
                    AddDistinct(currentTarget.RotationAngles, dso.Target.PositionAngle);
                }
            } else if (item is SwitchFilter switchFilter && !string.IsNullOrEmpty(switchFilter.Filter?.Name)) {
                var target = currentTarget ?? GetOrCreateTarget("Ungrouped", metadata, targetsByName);
                AddDistinctString(target.Filters, switchFilter.Filter.Name);
            } else if (item is CenterAndRotate centerAndRotate) {
                var target = currentTarget ?? GetOrCreateTarget("Ungrouped", metadata, targetsByName);
                AddDistinct(target.RotationAngles, centerAndRotate.PositionAngle);
            }

            if (item is ISequenceContainer container) {
                foreach (var child in container.Items) {
                    Walk(child, currentTarget, metadata, targetsByName);
                }
            }
        }

        private static NightFrontTargetMetadata GetOrCreateTarget(string name, NightFrontPlanMetadata metadata, Dictionary<string, NightFrontTargetMetadata> targetsByName) {
            if (targetsByName.TryGetValue(name, out var existing)) {
                return existing;
            }

            var target = new NightFrontTargetMetadata { TargetName = name };
            targetsByName[name] = target;
            metadata.Targets.Add(target);
            return target;
        }

        private static void AddDistinct(List<double> values, double value) {
            if (!values.Contains(value)) {
                values.Add(value);
            }
        }

        private static void AddDistinctString(List<string> values, string value) {
            if (!values.Contains(value)) {
                values.Add(value);
            }
        }
    }
}
