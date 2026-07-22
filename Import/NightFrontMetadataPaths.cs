using System;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Locates today's NightFront plan file and resolves the various sidecar file paths that live
    /// alongside it. Calibration metadata is a SINGLE, fixed-name, accumulating file
    /// (<see cref="CalibrationMetadataFileName"/>) per data folder - never tied to a plan filename or
    /// a date (see NightFrontMetadataStore). A date-derived name was tried and abandoned: it forked a
    /// second, disconnected metadata file whenever a writer (Update/Replan) ran after local midnight,
    /// because the stripped-out "today" token no longer matched the plan file's own (unchanging) night
    /// date. So this is the one place that names that file, the one place that lets
    /// calibration-consuming instructions/conditions find it without the user typing anything, and the
    /// one place that knows about every kind of NightFront-written sidecar file so FindTodaysPlanFile
    /// can exclude all of them from its "what's the actual plan file" scan. Also owns resolving the
    /// NightFront CLI's own path (ResolveCliPath/CoLocatedCliPath) - not a sidecar of a plan file, but
    /// the same kind of "where does the plugin find a file it depends on" logic this class already
    /// centralizes for everything else.
    /// </summary>
    public static class NightFrontMetadataPaths {
        private const string LiveMetadataSuffix = ".metadata.json";

        /// <summary>The single, fixed, undated filename every calibration-metadata read/write uses.
        /// Deliberately not derived from the plan filename or the date - see this class's own summary
        /// for the after-midnight forking bug that naming scheme caused.</summary>
        public const string CalibrationMetadataFileName = "calibration.metadata.json";

        /// <summary>Suffix for a NightFrontProgressSnapshot sidecar file (see
        /// NightFrontProgressSnapshotWriter) - a second kind of NightFront-written sidecar that, like
        /// the metadata file, must never be picked up by FindTodaysPlanFile as if it were an actual
        /// plan file. Unlike the metadata file, a progress snapshot is expected to be named with
        /// today's date (mirroring the plan file it was captured from), which is exactly the shape
        /// FindTodaysPlanFile searches for - so it needs its own explicit exclusion here rather than
        /// relying on IsMetadataFile's narrower check.</summary>
        private const string ProgressSnapshotSuffix = ".progress.json";

        /// <summary>Fixed (undated) filename NightFrontApp's exporter writes the exported
        /// ParetoEntry's (utilization, quality) coordinates to - one per export directory, shared
        /// across every night in a multi-night export (see NightFrontApp's ScheduleScreen.kt/
        /// SelectionPreference.kt), not per-plan-file like the progress snapshot. Consumed by
        /// Phase 3's NightFrontReplanInstruction, which passes it through to `NightFront replan`'s
        /// optional selection-preference argument.</summary>
        private const string SelectionPreferenceFileName = "selection.json";

        /// <summary>Fixed (undated) filename NightFrontApp's exporter writes its own input
        /// SessionConfig JSON to alongside the exported plan(s) - the plugin has no other way to
        /// learn where the config that produced tonight's plan lives, since it otherwise only ever
        /// sees the already-transformed NINA sequence JSON. Required by Phase 3's
        /// NightFrontReplanInstruction as `NightFront replan`'s first argument.</summary>
        private const string SessionConfigFileName = "session-config.json";

        /// <summary>Subfolder (relative to the NightFront data folder) that NightFrontReplanInstruction
        /// archives a snapshot of the plan file into immediately before overwriting it with a fresh
        /// replan, so the pre-replan plan stays available for later comparison rather than being lost.
        /// A subfolder, not a same-folder renamed copy, is deliberate: FindTodaysPlanFile's
        /// Directory.EnumerateFiles(folder, "*.json") only searches the top-level folder by default
        /// (no SearchOption.AllDirectories), so archived snapshots here are automatically invisible to
        /// "what's today's plan file" scanning - unlike every other same-folder sidecar this class
        /// manages, no explicit exclusion clause is needed to keep them out of that scan.</summary>
        private const string ReplanHistoryFolderName = "replan-history";

        /// <summary>
        /// Finds the plan JSON file in <paramref name="folder"/> whose name contains today's date
        /// (yyyy-MM-dd), excluding NightFront's own ".metadata.json"/"archived.metadata.json"/
        /// ".progress.json" sidecar files. Returns null if the folder is unset/missing or no match is
        /// found.
        /// </summary>
        public static string FindTodaysPlanFile(string folder, DateTime now) {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                return null;
            }

            var todayToken = now.ToString("yyyy-MM-dd");
            return Directory.EnumerateFiles(folder, "*.json")
                .Where(f => !IsMetadataFile(f) && !IsProgressSnapshotFile(f) && !IsSelectionPreferenceFile(f) && !IsSessionConfigFile(f))
                .FirstOrDefault(f => Path.GetFileName(f).Contains(todayToken));
        }

        /// <summary>Builds the progress-snapshot path for <paramref name="baseName"/> (typically the
        /// same date-stamped base name as the plan file it was captured from - see
        /// NightFrontJsonImporter/NightFrontUpdateInstruction). Intended as the one place a future
        /// caller (Phase 3's NightFrontReplanInstruction - see NightFrontApp's docs/DESIGN.md §
        /// Safety-Recovery Replan) derives where to write a NightFrontProgressSnapshot, rather than
        /// inventing an ad hoc path that FindTodaysPlanFile's exclusion filter above wouldn't
        /// recognize.</summary>
        public static string GetProgressSnapshotPath(string folder, string baseName) {
            return Path.Combine(folder, baseName + ProgressSnapshotSuffix);
        }

        /// <summary>Path to the shared, per-export selection-preference sidecar (see
        /// SelectionPreferenceFileName) - not per-plan-file, so unlike GetProgressSnapshotPath this
        /// takes no baseName.</summary>
        public static string GetSelectionPreferencePath(string folder) {
            return Path.Combine(folder, SelectionPreferenceFileName);
        }

        /// <summary>Path to the shared, per-export session-config sidecar (see
        /// SessionConfigFileName) - NightFrontReplanInstruction's source for `NightFront replan`'s
        /// required config-file argument.</summary>
        public static string GetSessionConfigPath(string folder) {
            return Path.Combine(folder, SessionConfigFileName);
        }

        /// <summary>The replan-history subfolder itself (see ReplanHistoryFolderName) - callers that
        /// need to ensure it exists (e.g. via Directory.CreateDirectory) before writing into it should
        /// use this rather than deriving the path themselves.</summary>
        public static string GetReplanHistoryFolder(string folder) {
            return Path.Combine(folder, ReplanHistoryFolderName);
        }

        /// <summary>Where to archive a snapshot of <paramref name="originalFileName"/> (the plan file
        /// about to be overwritten by a fresh replan) before it's overwritten. Timestamped so multiple
        /// replans the same night - a second safety interruption a few hours after the first - don't
        /// collide and each overwrite's "before" state is individually recoverable.</summary>
        public static string GetReplanHistoryPath(string folder, string originalFileName, DateTime timestamp) {
            return Path.Combine(GetReplanHistoryFolder(folder), $"{timestamp:yyyyMMdd-HHmmss}_{originalFileName}");
        }

        /// <summary>The single, fixed calibration-metadata path in <paramref name="folder"/>. Used by
        /// both writers (NightFrontUpdateInstruction/NightFrontReplanInstruction, where the file may
        /// not exist yet - the store creates it on first write) and consumers. No base name, no date,
        /// no per-plan-family variation - see this class's summary.</summary>
        public static string GetLiveMetadataPath(string folder) {
            return Path.Combine(folder, CalibrationMetadataFileName);
        }

        /// <summary>
        /// Resolves the calibration-metadata path for a calibration-consuming instruction/condition,
        /// returning null (with a human-readable <paramref name="issue"/>) if the folder isn't
        /// configured/missing or the file doesn't exist yet. "Doesn't exist yet" is a normal state -
        /// no images have been collected, so nothing has written the file - and callers are expected to
        /// exit gracefully with the message rather than treat it as an error.
        /// </summary>
        public static string ResolveExistingMetadataPath(string folder, out string issue) {
            issue = null;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                issue = "The NightFront data folder is not configured or does not exist.";
                return null;
            }

            var path = GetLiveMetadataPath(folder);
            if (!File.Exists(path)) {
                issue = $"No calibration metadata file ({CalibrationMetadataFileName}) exists yet in the NightFront data folder.";
                return null;
            }

            return path;
        }

        /// <summary>The nightfront-cli.exe path NightFrontApp's build.gradle.kts (graalvmNative,
        /// imageName "nightfront-cli") and NightFront.csproj's PostBuild step (BuildNativeCli.bat)
        /// agree to co-locate the native CLI at, alongside this plugin's own assembly - computed
        /// regardless of whether the file actually exists there, so ResolveCliPath and a
        /// diagnostic-only caller (Validate's "not found" message) can share one definition of
        /// "where it should be" instead of duplicating the Path.Combine.</summary>
        public static string CoLocatedCliPath(string assemblyLocation) {
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            return assemblyDir == null ? null : Path.Combine(assemblyDir, "nightfront-cli.exe");
        }

        /// <summary>
        /// Resolves the NightFront CLI path. Prefers <paramref name="overridePath"/> (typically
        /// Settings.Default.NightFrontCliPath) when it's set AND points at a file that actually
        /// exists - a manual override with no UI (hand-edited user.config), covering the case where
        /// the co-located build is missing, e.g. the sibling NightFrontApp checkout wasn't present
        /// when this plugin was built. Otherwise resolves to <see cref="CoLocatedCliPath"/> - built
        /// and copied there automatically by NightFront.csproj's PostBuild step (BuildNativeCli.bat),
        /// so the common case needs no configuration at all. Returns null if neither resolves to an
        /// existing file. <paramref name="assemblyLocation"/> is threaded through (rather than
        /// calling Assembly.GetExecutingAssembly().Location directly) so this stays a small, pure,
        /// independently testable function, matching this class's other path-resolution helpers.
        /// </summary>
        public static string ResolveCliPath(string overridePath, string assemblyLocation) {
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) {
                return overridePath;
            }
            var coLocatedPath = CoLocatedCliPath(assemblyLocation);
            return coLocatedPath != null && File.Exists(coLocatedPath) ? coLocatedPath : null;
        }

        private static bool IsMetadataFile(string path) {
            return path.EndsWith(LiveMetadataSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProgressSnapshotFile(string path) {
            return path.EndsWith(ProgressSnapshotSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSelectionPreferenceFile(string path) {
            return string.Equals(Path.GetFileName(path), SelectionPreferenceFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSessionConfigFile(string path) {
            return string.Equals(Path.GetFileName(path), SessionConfigFileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
