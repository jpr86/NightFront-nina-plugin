using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Utility.Notification;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Live, bindable per-target summary row for NightFrontContainer's sequencer UI. Built once, from
    /// a target's already-imported instructions, when the container is populated:
    /// - Name/coordinates/rotation/window-end are static snapshots of the imported plan.
    /// - WindowStart is set afterward by NightFrontContainer, chained from the previous target's
    ///   WindowEnd (a target's own subtree only carries its end-of-window boundary).
    /// - Status/CompletedCount stay live: Status forwards the target container's own execution
    ///   status; CompletedCount is recomputed whenever a tracked LoopCondition's CompletedIterations
    ///   changes (for looped exposure blocks) or a tracked bare TakeExposure's Status changes (for
    ///   single-exposure segments, which NightFrontApp emits without a wrapping LoopCondition).
    /// </summary>
    public class NightFrontTargetSummary : INotifyPropertyChanged, IDisposable {
        private readonly DeepSkyObjectContainer target;
        private readonly List<LoopCondition> watchedLoops = new List<LoopCondition>();
        private readonly List<TakeExposure> watchedUnloopedExposures = new List<TakeExposure>();

        public NightFrontTargetSummary(DeepSkyObjectContainer target) {
            this.target = target;

            Name = string.IsNullOrWhiteSpace(target.Target?.TargetName) ? target.Name : target.Target.TargetName;
            Coordinates = target.Target?.InputCoordinates;
            RotationAngle = target.Target?.PositionAngle ?? 0d;

            target.PropertyChanged += Target_PropertyChanged;

            // NINA's TimeCondition.RemainingTime is computed from its DateTimeProvider, which may
            // not be fully initialized for a condition built directly by the importer rather than
            // through the normal sequencer UI flow. A failure here must not prevent the rest of this
            // row (name/coordinates/rotation, and the container's overall PopulateItems) from
            // succeeding - leave WindowEnd unknown instead.
            try {
                var timeCondition = FindTimeCondition(target);
                if (timeCondition != null) {
                    WindowEnd = DateTime.Now.Add(timeCondition.RemainingTime);
                }
            } catch (Exception ex) {
                WindowEnd = null;
                Notification.ShowWarning($"NightFront: could not determine the scheduled end time for '{Name}': {ex.Message}");
            }

            // Same defensive posture for progress tracking: an unexpected shape in the imported
            // tree shouldn't take down summary construction for this target.
            try {
                CollectExposureTrackers(target);
                PlannedCount = watchedLoops.Sum(l => Math.Max(l.Iterations, 0)) + watchedUnloopedExposures.Count;
                completedCount = ComputeCompletedCount();

                foreach (var loop in watchedLoops) {
                    loop.PropertyChanged += Tracked_PropertyChanged;
                }
                foreach (var exposure in watchedUnloopedExposures) {
                    exposure.PropertyChanged += Tracked_PropertyChanged;
                }
            } catch (Exception ex) {
                // PlannedCount/CompletedCount stay at their defaults (0).
                Notification.ShowWarning($"NightFront: could not determine exposure progress for '{Name}': {ex.Message}");
            }
        }

        public string Name { get; }

        public InputCoordinates Coordinates { get; }

        public double RotationAngle { get; }

        /// <summary>Set once by NightFrontContainer after all targets' summaries are built, chained
        /// from the previous target's WindowEnd. Null for the first target (its real start isn't
        /// known until the sequence actually begins).</summary>
        public DateTime? WindowStart { get; internal set; }

        public DateTime? WindowEnd { get; }

        public int PlannedCount { get; }

        private int completedCount;

        public int CompletedCount {
            get => completedCount;
            private set {
                if (completedCount == value) {
                    return;
                }
                completedCount = value;
                RaisePropertyChanged();
            }
        }

        public SequenceEntityStatus Status => target.Status;

        private void Target_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Status) || string.IsNullOrEmpty(e.PropertyName)) {
                RaisePropertyChanged(nameof(Status));
            }
        }

        private void Tracked_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            CompletedCount = ComputeCompletedCount();
        }

        private int ComputeCompletedCount() {
            return watchedLoops.Sum(l => Math.Max(l.CompletedIterations, 0))
                + watchedUnloopedExposures.Count(e => e.Status == SequenceEntityStatus.FINISHED);
        }

        private void CollectExposureTrackers(ISequenceContainer container) {
            var loopCondition = (container as IConditionable)?.Conditions?.OfType<LoopCondition>().FirstOrDefault();
            if (loopCondition != null) {
                watchedLoops.Add(loopCondition);
            }

            foreach (var item in container.Items) {
                if (item is TakeExposure exposure) {
                    if (loopCondition == null) {
                        watchedUnloopedExposures.Add(exposure);
                    }
                } else if (item is ISequenceContainer nested) {
                    CollectExposureTrackers(nested);
                }
            }
        }

        private static TimeCondition FindTimeCondition(ISequenceContainer container) {
            var found = (container as IConditionable)?.Conditions?.OfType<TimeCondition>().FirstOrDefault();
            if (found != null) {
                return found;
            }

            foreach (var item in container.Items) {
                if (item is ISequenceContainer nested) {
                    var nestedFound = FindTimeCondition(nested);
                    if (nestedFound != null) {
                        return nestedFound;
                    }
                }
            }

            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Detaches from the live sequence objects this row subscribed to. NightFrontContainer calls
        /// this on every previous summary before rebuilding, so replaced targets/loops/exposures
        /// don't keep a stale summary (and everything it references) alive via a live event handler.
        /// </summary>
        public void Dispose() {
            target.PropertyChanged -= Target_PropertyChanged;
            foreach (var loop in watchedLoops) {
                loop.PropertyChanged -= Tracked_PropertyChanged;
            }
            foreach (var exposure in watchedUnloopedExposures) {
                exposure.PropertyChanged -= Tracked_PropertyChanged;
            }
        }
    }
}
