using System;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Locates today's NightFront plan file and derives/resolves the calibration-metadata and
    /// progress-snapshot sidecar file paths associated with it. Metadata is no longer tied to a
    /// single night's dated plan filename (it accumulates across nights - see NightFrontMetadataStore),
    /// so this is the one place that translates "today's dated plan file" into "the accumulating
    /// metadata file for that plan family," and the one place that lets calibration-consuming
    /// instructions/conditions find that same file without the user re-typing its name everywhere.
    /// It's also the one place that knows about every kind of NightFront-written sidecar file, so
    /// FindTodaysPlanFile can exclude all of them from its "what's the actual plan file" scan.
    /// Also owns resolving the NightFront CLI's own path (ResolveCliPath/CoLocatedCliPath) - not a
    /// sidecar of a plan file, but the same kind of "where does the plugin find a file it depends
    /// on" logic this class already centralizes for everything else.
    /// </summary>
    public static class NightFrontMetadataPaths {
        private const string LiveMetadataSuffix = ".metadata.json";
        private const string ArchivedMetadataFileName = "archived.metadata.json";
        private const string ReservedArchiveBaseName = "archived";

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

        /// <summary>
        /// Finds the plan JSON file in <paramref name="folder"/> most recently written to, with no
        /// date-token match required - the same sidecar exclusions as <see cref="FindTodaysPlanFile"/>,
        /// just without its "name contains today's date" filter. A replan that runs after local
        /// midnight has a "today" that no longer matches the date baked into the night's own plan
        /// filename - that filename is fixed at whatever date NightFrontApp's exporter (or an earlier
        /// same-night replan) last wrote, not the date the replan itself happens to run on - so
        /// FindTodaysPlanFile alone would find nothing and a caller would wrongly conclude no plan
        /// exists yet. Callers that want "the plan currently in effect regardless of what today's date
        /// is" (see NightFrontReplanInstruction) should try FindTodaysPlanFile first (the common,
        /// cheap, unambiguous case) and fall back to this only when that returns null. Returns null if
        /// the folder is unset/missing or no candidate file exists.
        /// </summary>
        public static string FindMostRecentPlanFile(string folder) {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                return null;
            }

            return Directory.EnumerateFiles(folder, "*.json")
                .Where(f => !IsMetadataFile(f) && !IsProgressSnapshotFile(f) && !IsSelectionPreferenceFile(f) && !IsSessionConfigFile(f))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>Builds the progress-snapshot path for <paramref name="baseName"/> (typically the
        /// same date-stamped base name as the plan file it was captured from - see
        /// NightFrontJsonImporter/NightFrontUpdateInstruction). Intended as the one place a future
        /// caller (Phase 3's NightFrontReplanInstruction - see todos/nina-safety-delay-plan.md) derives
        /// where to write a NightFrontProgressSnapshot, rather than inventing an ad hoc path that
        /// FindTodaysPlanFile's exclusion filter above wouldn't recognize.</summary>
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

        /// <summary>
        /// Strips exactly one occurrence of <paramref name="todayToken"/> (plus at most one adjacent
        /// '_'/'-' separator on each side) from <paramref name="planFileBaseName"/>, so the same
        /// metadata file is reused every night a plan with that date substituted in is imported (e.g.
        /// "TargetsForTonight_2026-07-06" with token "2026-07-06" -> "TargetsForTonight"). Not a
        /// general date parser - matches the one concrete filename shape NightFrontApp's exporter
        /// produces. Returns the base name unchanged if the token isn't found verbatim, and falls
        /// back to "NightFront" if stripping would leave nothing or would leave the reserved
        /// "archived" name (which collides with a legacy "archived.metadata.json" sidecar file that
        /// may still be sitting in the folder from before metadata completion tracking moved into the
        /// single live file - see GetLiveMetadataPath).
        /// </summary>
        public static string DeriveStableBaseName(string planFileBaseName, string todayToken) {
            if (string.IsNullOrEmpty(planFileBaseName) || string.IsNullOrEmpty(todayToken)) {
                return planFileBaseName;
            }

            var idx = planFileBaseName.IndexOf(todayToken, StringComparison.Ordinal);
            if (idx < 0) {
                return planFileBaseName;
            }

            var before = planFileBaseName.Substring(0, idx);
            var after = planFileBaseName.Substring(idx + todayToken.Length);
            if (before.Length > 0 && (before[before.Length - 1] == '_' || before[before.Length - 1] == '-')) {
                before = before.Substring(0, before.Length - 1);
            }
            if (after.Length > 0 && (after[0] == '_' || after[0] == '-')) {
                after = after.Substring(1);
            }

            var stable = before + after;
            if (string.IsNullOrEmpty(stable) || string.Equals(stable, ReservedArchiveBaseName, StringComparison.OrdinalIgnoreCase)) {
                return "NightFront";
            }
            return stable;
        }

        /// <summary>Builds the live metadata path for <paramref name="baseName"/>. Throws if
        /// <paramref name="baseName"/> is the reserved "archived" name, which would otherwise resolve
        /// to the same path as a legacy "archived.metadata.json" sidecar file that older plugin
        /// versions wrote (see IsArchiveFile) and corrupt it.</summary>
        public static string GetLiveMetadataPath(string folder, string baseName) {
            if (string.Equals(baseName, ReservedArchiveBaseName, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("\"archived\" cannot be used as a NightFront calibration-metadata name - it collides with the shared archived.metadata.json file. Choose a different name.", nameof(baseName));
            }
            return Path.Combine(folder, baseName + LiveMetadataSuffix);
        }

        /// <summary>
        /// Resolves which live metadata file a calibration-consuming instruction/condition should
        /// use. <paramref name="explicitBaseName"/> wins verbatim if set (rejected via
        /// <paramref name="issue"/> if it's the reserved "archived" name). Otherwise scans the folder
        /// for "*.metadata.json" files (excluding a legacy "archived.metadata.json" sidecar file, if
        /// one is still present from before completion tracking moved into the single live file):
        /// exactly one match resolves automatically; zero or more than one leaves
        /// <paramref name="issue"/> populated with a message naming the candidates (or noting none
        /// exist) and returns null.
        /// </summary>
        public static string ResolveBaseName(string folder, string explicitBaseName, out string issue) {
            issue = null;

            if (!string.IsNullOrWhiteSpace(explicitBaseName)) {
                if (string.Equals(explicitBaseName, ReservedArchiveBaseName, StringComparison.OrdinalIgnoreCase)) {
                    issue = "\"archived\" cannot be used as a NightFront calibration-metadata name - it collides with the shared archived.metadata.json file. Choose a different name.";
                    return null;
                }
                return explicitBaseName;
            }

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                issue = "The NightFront data folder is not configured or does not exist.";
                return null;
            }

            var candidates = Directory.EnumerateFiles(folder, "*" + LiveMetadataSuffix)
                .Where(f => !IsArchiveFile(f))
                .Select(f => Path.GetFileName(f).Substring(0, Path.GetFileName(f).Length - LiveMetadataSuffix.Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 1) {
                return candidates[0];
            }

            issue = candidates.Count == 0
                ? "No calibration metadata file was found in the NightFront data folder."
                : $"Multiple calibration metadata files found in the NightFront data folder; set a specific name to disambiguate: {string.Join(", ", candidates)}.";
            return null;
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

        /// <summary>NightFront no longer writes this file itself - completed calibration requirements
        /// stay in the live file with FlatsCompletedDate stamped instead of being archived elsewhere.
        /// This only guards against a leftover "archived.metadata.json" from an older plugin version
        /// being misdetected as a live metadata candidate.</summary>
        private static bool IsArchiveFile(string path) {
            return string.Equals(Path.GetFileName(path), ArchivedMetadataFileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
