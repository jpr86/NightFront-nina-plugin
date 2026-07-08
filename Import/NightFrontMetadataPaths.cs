using System;
using System.IO;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Locates today's NightFront plan file and derives/resolves the calibration-metadata file paths
    /// associated with it. Metadata is no longer tied to a single night's dated plan filename (it
    /// accumulates across nights - see NightFrontMetadataStore), so this is the one place that
    /// translates "today's dated plan file" into "the accumulating metadata file for that plan
    /// family," and the one place that lets calibration-consuming instructions/conditions find that
    /// same file without the user re-typing its name everywhere.
    /// </summary>
    public static class NightFrontMetadataPaths {
        private const string LiveMetadataSuffix = ".metadata.json";
        private const string ArchivedMetadataFileName = "archived.metadata.json";
        private const string ReservedArchiveBaseName = "archived";

        /// <summary>
        /// Finds the plan JSON file in <paramref name="folder"/> whose name contains today's date
        /// (yyyy-MM-dd), excluding NightFront's own ".metadata.json"/"archived.metadata.json" sidecar
        /// files. Returns null if the folder is unset/missing or no match is found.
        /// </summary>
        public static string FindTodaysPlanFile(string folder, DateTime now) {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) {
                return null;
            }

            var todayToken = now.ToString("yyyy-MM-dd");
            return Directory.EnumerateFiles(folder, "*.json")
                .Where(f => !IsMetadataFile(f))
                .FirstOrDefault(f => Path.GetFileName(f).Contains(todayToken));
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

        private static bool IsMetadataFile(string path) {
            return path.EndsWith(LiveMetadataSuffix, StringComparison.OrdinalIgnoreCase);
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
