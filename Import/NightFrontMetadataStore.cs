using Newtonsoft.Json;
using NINA.Core.Utility.Notification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Centralizes every read/modify/write of a NightFrontPlanMetadata file (both the live
    /// "&lt;baseName&gt;.metadata.json" file and the shared "archived.metadata.json" file). Introduced
    /// so that NightFrontMetadataRecorder, and the calibration-consuming flat instructions, can all
    /// safely interleave reads/writes within one running sequence instead of each holding its own
    /// private in-memory snapshot (the recorder used to build one NightFrontPlanMetadata once in its
    /// constructor and blindly overwrite the file from it on every write - a stale snapshot that would
    /// clobber any change made by another component in between).
    ///
    /// A single process-wide lock guards every call into this class, rather than one lock per file
    /// path. This is deliberate: the archive file is a single file shared across every live metadata
    /// file's operations (per-BaseName live files, one shared archive), so a per-live-path lock would
    /// not correctly serialize two different live files' operations that both touch that one shared
    /// archive concurrently. Store operations are small, infrequent (JSON files of a handful of
    /// entries, touched on rotation-complete events and loop-condition checks, not in a hot loop), so
    /// giving up cross-file-name parallelism for correctness is the right trade here.
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
        /// Adds a calibration requirement iff no entry in EITHER the live or archived file already
        /// covers (filter case-insensitive, rotation angle within 1 degree, exact gain, exact
        /// offset). Returns whether it was added.
        /// </summary>
        public static bool TryAddCalibrationRequirement(string livePath, string archivedPath, string filter, double angle, int gain, int offset) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                var archived = LoadUnlocked(archivedPath);

                var alreadyCovered =
                    live.CalibrationRequirements.Any(r => Matches(r, filter, angle, gain, offset)) ||
                    archived.CalibrationRequirements.Any(r => Matches(r, filter, angle, gain, offset));
                if (alreadyCovered) {
                    return false;
                }

                live.CalibrationRequirements.Add(new NightFrontCalibrationRequirement {
                    Filter = filter,
                    RotationAngle = angle,
                    Gain = gain,
                    Offset = offset
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
        /// Atomically removes and returns the head calibration requirement, or null if none remain.
        /// Two concurrent callers (e.g. two sequence branches sharing one metadata file) can never
        /// both claim the same entry - the second caller sees it already gone and gets the next entry
        /// (or null).
        /// </summary>
        public static NightFrontCalibrationRequirement ClaimNext(string livePath) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                if (live.CalibrationRequirements.Count == 0) {
                    return null;
                }
                var claimed = live.CalibrationRequirements[0];
                live.CalibrationRequirements.RemoveAt(0);
                if (!SaveUnlocked(live, livePath)) {
                    // If the removal didn't actually persist, the caller must not treat this as a
                    // successful claim - returning `claimed` here would let it proceed to shoot a
                    // flat while the entry silently reappears on the next read, reshooting forever.
                    throw new IOException($"Could not persist claiming the next calibration requirement to '{livePath}'.");
                }
                return claimed;
            }
        }

        /// <summary>Read-only equivalent of ClaimNext - the current head entry, without removing it.</summary>
        public static NightFrontCalibrationRequirement PeekNext(string livePath) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                return live.CalibrationRequirements.Count > 0 ? live.CalibrationRequirements[0] : null;
            }
        }

        /// <summary>Success path for an entry previously returned by ClaimNext: appends it to the
        /// archive file. The entry is already removed from the live file, so this never needs to
        /// re-locate it there.</summary>
        public static void ArchiveClaimed(string archivedPath, NightFrontCalibrationRequirement claimed) {
            lock (syncLock) {
                var archived = LoadUnlocked(archivedPath);
                archived.CalibrationRequirements.Add(claimed);
                archived.GeneratedAtUtc = DateTime.UtcNow;
                if (!SaveUnlocked(archived, archivedPath)) {
                    // Throwing here (rather than swallowing) lets NightFrontFlatsInstructionBase's
                    // catch block restore the claimed entry to the live file instead of losing it.
                    throw new IOException($"Could not persist archiving the completed calibration requirement to '{archivedPath}'.");
                }
            }
        }

        /// <summary>Failure/cancellation path for an entry previously returned by ClaimNext:
        /// re-inserts it at the head of the live file so a failed/cancelled flat run doesn't lose the
        /// requirement.</summary>
        public static void RestoreClaimed(string livePath, NightFrontCalibrationRequirement claimed) {
            lock (syncLock) {
                var live = LoadUnlocked(livePath);
                live.CalibrationRequirements.Insert(0, claimed);
                if (!SaveUnlocked(live, livePath)) {
                    // Nothing left to fall back to at this point - surface loudly rather than
                    // silently losing the requirement.
                    throw new IOException($"Could not persist restoring the calibration requirement to '{livePath}' after a failed/cancelled run.");
                }
            }
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

            try {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<NightFrontPlanMetadata>(json) ?? new NightFrontPlanMetadata();
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: could not read calibration metadata file '{path}': {ex.Message}");
                return new NightFrontPlanMetadata();
            }
        }

        /// <summary>Returns whether the write actually succeeded. Callers that need the file to
        /// reflect a change for correctness (ClaimNext/ArchiveClaimed/RestoreClaimed) must check this
        /// and not treat a failed write as a persisted one; callers invoked from an event-handler
        /// context with no caller able to act on failure (RecordRunStarted/UpsertTargetMetadata/
        /// RecordMeasuredRotationAngle/TryAddCalibrationRequirement) treat this as best-effort, same
        /// as the warning notification already shown here.</summary>
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
