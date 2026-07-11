using Newtonsoft.Json;
using NINA.Core.Utility.Notification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Centralizes every read/modify/write of a NightFrontPlanMetadata file. Introduced so that
    /// NightFrontMetadataRecorder, and the calibration-consuming flat instructions/conditions, can all
    /// safely interleave reads/writes within one running sequence instead of each holding its own
    /// private in-memory snapshot (the recorder used to build one NightFrontPlanMetadata once in its
    /// constructor and blindly overwrite the file from it on every write - a stale snapshot that would
    /// clobber any change made by another component in between).
    ///
    /// There is a single, accumulating file per plan family - no separate archive. A completed
    /// calibration requirement stays in the same CalibrationRequirements list with its
    /// FlatsCompletedDate stamped; PeekNext/ClaimNext simply skip anything already completed (or
    /// currently claimed by another in-flight flats instruction). PruneStaleCompleted is the only
    /// thing that ever removes an entry, once it's been completed for longer than the configured
    /// refresh window - at which point NightFrontMetadataRecorder will naturally re-add it as a fresh,
    /// outstanding requirement the next time that filter/angle/gain/offset combination is seen.
    ///
    /// A single process-wide lock guards every call into this class, rather than one lock per file
    /// path. Store operations are small, infrequent (JSON files of a handful of entries, touched on
    /// exposure-complete events and loop-condition checks, not in a hot loop), so a single lock is
    /// simpler and the loss of cross-file-name parallelism doesn't matter in practice.
    /// </summary>
    public static class NightFrontMetadataStore {
        private const double DuplicateAngleToleranceDegrees = 1.0;
        private static readonly object syncLock = new object();

        /// <summary>Loads a metadata file, or an empty instance if it doesn't exist or fails to parse.</summary>
        public static NightFrontPlanMetadata Load(string path) {
            lock (syncLock) {
                return LoadUnlocked(path);
            }
        }

        /// <summary>Stamps the live file with which run last touched it - called once per
        /// NightFrontMetadataRecorder construction (i.e. once per Nightly Update run), independent of
        /// whether any new calibration requirement is actually added during that run.</summary>
        public static void RecordRunStarted(string livePath, string sourcePlanFileName) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                live.Date = DateTime.Now.ToString("yyyy-MM-dd");
                live.SourcePlanFile = sourcePlanFileName;
                live.GeneratedAtUtc = DateTime.UtcNow;
                SaveUnlocked(live, livePath);
            }
        }

        /// <summary>
        /// Adds a calibration requirement iff no entry already in the file covers (filter
        /// case-insensitive, rotation angle within 1 degree, exact gain, exact offset) - completed
        /// entries still count toward this check, since they aren't removed on completion. Returns
        /// whether it was added.
        /// </summary>
        public static bool TryAddCalibrationRequirement(string livePath, string filter, double angle, int gain, int offset) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);

                var alreadyCovered = live.CalibrationRequirements.Any(r => Matches(r, filter, angle, gain, offset));
                if (alreadyCovered) {
                    return false;
                }

                live.CalibrationRequirements.Add(new NightFrontCalibrationRequirement {
                    Id = Guid.NewGuid(),
                    Filter = filter,
                    RotationAngle = angle,
                    Gain = gain,
                    Offset = offset,
                    DateAdded = DateTime.Now
                });
                live.GeneratedAtUtc = DateTime.UtcNow;
                SaveUnlocked(live, livePath);
                return true;
            }
        }

        /// <summary>Adds a new target row, or replaces the existing one for the same target name (by
        /// name, not object identity - a target re-imported on a later night upserts rather than
        /// appending a duplicate row).</summary>
        public static void UpsertTargetMetadata(string livePath, string targetName, IEnumerable<string> filters) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var filterList = filters.ToList();
                var existing = live.Targets.FirstOrDefault(t => string.Equals(t.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
                if (existing != null) {
                    existing.Filters = filterList;
                } else {
                    live.Targets.Add(new NightFrontTargetMetadata { TargetName = targetName, Filters = filterList });
                }
                SaveUnlocked(live, livePath);
            }
        }

        /// <summary>Records a target's measured rotation angle, upserting the target row by name if
        /// UpsertTargetMetadata hasn't already created it.</summary>
        public static void RecordMeasuredRotationAngle(string livePath, string targetName, double angle) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var existing = live.Targets.FirstOrDefault(t => string.Equals(t.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
                if (existing == null) {
                    existing = new NightFrontTargetMetadata { TargetName = targetName };
                    live.Targets.Add(existing);
                }
                existing.MeasuredRotationAngle = angle;
                SaveUnlocked(live, livePath);
            }
        }

        /// <summary>
        /// Atomically marks the next outstanding calibration requirement as claimed and returns it, or
        /// null if none remain (or none remain within <paramref name="scopedToAngleDegrees"/>, if
        /// given). Two concurrent callers (e.g. two sequence branches sharing one metadata file) can
        /// never both claim the same entry - the second caller sees it already claimed and gets the
        /// next one (or null). <paramref name="filterOrder"/> (may be null/empty) ranks entries by
        /// filter name - see SelectNext.
        /// </summary>
        public static NightFrontCalibrationRequirement ClaimNext(string livePath, IReadOnlyList<string> filterOrder, double? scopedToAngleDegrees = null) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var next = SelectNext(live, filterOrder, scopedToAngleDegrees);
                if (next == null) {
                    return null;
                }
                next.Claimed = true;
                if (!SaveUnlocked(live, livePath)) {
                    // If the claim didn't actually persist, the caller must not treat this as a
                    // successful claim - returning `next` here would let it proceed to shoot a flat
                    // while the claim silently reverts on the next read, reshooting forever.
                    throw new IOException($"Could not persist claiming the next calibration requirement to '{livePath}'.");
                }
                return next;
            }
        }

        /// <summary>Read-only equivalent of ClaimNext - the current next outstanding entry, without
        /// claiming it.</summary>
        public static NightFrontCalibrationRequirement PeekNext(string livePath, IReadOnlyList<string> filterOrder, double? scopedToAngleDegrees = null) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                return SelectNext(live, filterOrder, scopedToAngleDegrees);
            }
        }

        /// <summary>Success path for an entry previously returned by ClaimNext: stamps its
        /// FlatsCompletedDate and clears Claimed. The entry stays in the file (no archive) so it
        /// keeps blocking TryAddCalibrationRequirement from re-adding a duplicate until it's eventually
        /// pruned by PruneStaleCompleted.</summary>
        public static void MarkCompleted(string livePath, Guid requirementId) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var entry = live.CalibrationRequirements.FirstOrDefault(r => r.Id == requirementId);
                if (entry == null) {
                    return;
                }
                entry.FlatsCompletedDate = DateTime.Now;
                entry.Claimed = false;
                if (!SaveUnlocked(live, livePath)) {
                    // Throwing here (rather than swallowing) lets NightFrontFlatsInstructionBase's
                    // catch block release the claim instead of leaving it stuck claimed forever.
                    throw new IOException($"Could not persist completing the calibration requirement in '{livePath}'.");
                }
            }
        }

        /// <summary>Failure/cancellation path for an entry previously returned by ClaimNext: clears
        /// Claimed so it's eligible to be claimed again.</summary>
        public static void ReleaseClaim(string livePath, Guid requirementId) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var entry = live.CalibrationRequirements.FirstOrDefault(r => r.Id == requirementId);
                if (entry == null) {
                    return;
                }
                entry.Claimed = false;
                if (!SaveUnlocked(live, livePath)) {
                    // Nothing left to fall back to at this point - surface loudly rather than
                    // silently leaving the requirement claimed forever.
                    throw new IOException($"Could not persist releasing the calibration requirement's claim in '{livePath}' after a failed/cancelled run.");
                }
            }
        }

        /// <summary>Removes every calibration requirement that's been completed for longer than
        /// <paramref name="refreshDays"/>. Entries that have never been completed are never touched by
        /// this - they simply keep waiting. Best-effort, like the other write paths driven from an
        /// event/run context with no caller able to act on a failure.</summary>
        public static void PruneStaleCompleted(string livePath, int refreshDays) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var cutoff = DateTime.Now.AddDays(-refreshDays);
                var removed = live.CalibrationRequirements.RemoveAll(r => r.FlatsCompletedDate != null && r.FlatsCompletedDate.Value < cutoff);
                if (removed > 0) {
                    live.GeneratedAtUtc = DateTime.UtcNow;
                    SaveUnlocked(live, livePath);
                }
            }
        }

        /// <summary>
        /// Picks the next outstanding (not completed, not currently claimed) requirement: ranked by
        /// its filter's position in <paramref name="filterOrder"/> (case-insensitive; a filter absent
        /// from filterOrder ranks after every filter that is listed), then by original list order as a
        /// stable tie-break (LINQ's OrderBy is stable, so this falls out of Where/OrderBy without an
        /// explicit secondary sort). A null/empty filterOrder ranks every filter equally, so original
        /// list order alone determines the result - i.e. today's FIFO behavior.
        ///
        /// When <paramref name="scopedToAngleDegrees"/> is given, candidates are first narrowed to
        /// those whose RotationAngle rounds (MidpointRounding.AwayFromZero) to the same whole degree
        /// as it does - the exact same "rounded to the nearest whole degree" standard
        /// NightFrontWhileSameRotationCondition's own doc comment already documents, reused here
        /// rather than introducing a second, differently-shaped tolerance concept (e.g. a raw ±1°
        /// window, which can disagree with rounding right at a half-degree boundary). Returns null if
        /// nothing outstanding matches that angle, even if other angles still have outstanding work -
        /// this is what lets a scoped caller (NightFrontWhileSameRotationCondition) tell "nothing left
        /// at this angle" apart from "nothing left at all."
        ///
        /// This scoping exists to fix a real bug: without it, once the single highest-filterOrder-rank
        /// entry at the current angle is completed, the next-best entry by filterOrder ALONE could sit
        /// at a completely different angle (e.g. two "L" requirements at different angles, with a "B"
        /// requirement genuinely at the current angle ranking below "L") - SelectNext would jump to
        /// that other angle's entry, which NightFrontWhileSameRotationCondition then (correctly, given
        /// what it was handed) sees as an angle change and stops on, skipping the same-angle "B" entry
        /// that never got a turn. Confirmed against a real production metadata file where exactly this
        /// happened (an "L" and a "B" requirement within 0.05deg of each other, "L" ranked first and
        /// completed, "B" left outstanding because a second, unrelated "L" requirement at a ~45deg
        /// different angle outranked it in the unscoped selection).
        /// </summary>
        private static NightFrontCalibrationRequirement SelectNext(NightFrontPlanMetadata live, IReadOnlyList<string> filterOrder, double? scopedToAngleDegrees = null) {
            var candidates = live.CalibrationRequirements.Where(r => r.FlatsCompletedDate == null && !r.Claimed);

            if (scopedToAngleDegrees.HasValue) {
                var scopedRounded = Math.Round(scopedToAngleDegrees.Value, MidpointRounding.AwayFromZero);
                candidates = candidates.Where(r => Math.Round(r.RotationAngle, MidpointRounding.AwayFromZero) == scopedRounded);
            }

            return candidates
                .OrderBy(r => FilterRank(r.Filter, filterOrder))
                .FirstOrDefault();
        }

        private static int FilterRank(string filter, IReadOnlyList<string> filterOrder) {
            if (filterOrder == null || filterOrder.Count == 0) {
                return 0;
            }
            for (var i = 0; i < filterOrder.Count; i++) {
                if (string.Equals(filterOrder[i], filter, StringComparison.OrdinalIgnoreCase)) {
                    return i;
                }
            }
            return filterOrder.Count;
        }

        private static bool Matches(NightFrontCalibrationRequirement requirement, string filter, double angle, int gain, int offset) {
            return string.Equals(requirement.Filter, filter, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(requirement.RotationAngle - angle) < DuplicateAngleToleranceDegrees
                && requirement.Gain == gain
                && requirement.Offset == offset;
        }

        private static NightFrontPlanMetadata LoadUnlocked(string path) {
            if (!File.Exists(path)) {
                return new NightFrontPlanMetadata();
            }

            NightFrontPlanMetadata metadata;
            try {
                var json = File.ReadAllText(path);
                metadata = JsonConvert.DeserializeObject<NightFrontPlanMetadata>(json) ?? new NightFrontPlanMetadata();
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: could not read calibration metadata file '{path}': {ex.Message}");
                return new NightFrontPlanMetadata();
            }

            // Entries written before Id existed come back as Guid.Empty - assign each a real one so
            // ClaimNext/MarkCompleted/ReleaseClaim can address them individually. This becomes
            // permanent the next time anything saves the file; a plain Load (no save) regenerating a
            // fresh id on every call is harmless since nothing keys off a read-only id.
            foreach (var requirement in metadata.CalibrationRequirements) {
                if (requirement.Id == Guid.Empty) {
                    requirement.Id = Guid.NewGuid();
                }
            }

            return metadata;
        }

        /// <summary>Returns whether the write actually succeeded. Callers that need the file to
        /// reflect a change for correctness (ClaimNext/MarkCompleted/ReleaseClaim) must check this and
        /// not treat a failed write as a persisted one; callers invoked from an event-handler context
        /// with no caller able to act on failure (RecordRunStarted/UpsertTargetMetadata/
        /// RecordMeasuredRotationAngle/TryAddCalibrationRequirement/PruneStaleCompleted) treat this as
        /// best-effort, same as the warning notification already shown here.</summary>
        private static bool SaveUnlocked(NightFrontPlanMetadata metadata, string path) {
            try {
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));
                File.Move(tempPath, path, overwrite: true);
                return true;
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: failed to write calibration metadata file '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
