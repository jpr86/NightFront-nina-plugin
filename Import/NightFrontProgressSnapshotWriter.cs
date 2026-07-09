using Newtonsoft.Json;
using NINA.Core.Utility.Notification;
using System;
using System.IO;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Writes a NightFrontProgressSnapshot to disk as its own single-purpose JSON file. Unlike
    /// NightFrontMetadataStore's accumulating, multi-writer calibration file, a progress snapshot is
    /// produced fresh and whole by a single writer each time it's needed (a future safety-recovery
    /// replan step - see todos/nina-safety-delay-plan.md Phase 3), so no read-modify-write locking is
    /// needed here. Callers should derive <c>path</c> via NightFrontMetadataPaths.GetProgressSnapshotPath
    /// rather than inventing their own - that's the one place FindTodaysPlanFile's "what's the actual
    /// plan file" scan is kept in sync with every kind of NightFront-written sidecar file, so a
    /// snapshot written to an unrecognized name/location risks being misidentified as a plan file.
    /// </summary>
    public static class NightFrontProgressSnapshotWriter {

        /// <summary>Writes <paramref name="snapshot"/> to <paramref name="path"/> atomically (write to
        /// a uniquely-named temp file, then move over the destination) so a reader never observes a
        /// partially-written file - same pattern as NightFrontMetadataStore's own save path. The temp
        /// file name includes a fresh Guid (not just a fixed ".tmp" suffix) so two overlapping calls
        /// targeting the same path - e.g. a timed-out replan attempt retried while the previous
        /// attempt's write is still in flight - can't clobber each other's temp file; each call's
        /// File.Move is independent, so the last one to move simply becomes the final content, and
        /// neither call can observe or report the other's write as its own. Returns whether the write
        /// succeeded; best-effort, since this is expected to run from event/instruction contexts with
        /// no caller positioned to retry.</summary>
        public static bool Write(string path, NightFrontProgressSnapshot snapshot) {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                File.Move(tempPath, path, overwrite: true);
                return true;
            } catch (Exception ex) {
                Notification.ShowWarning($"NightFront: failed to write progress snapshot file '{path}': {ex.Message}");
                try {
                    File.Delete(tempPath);
                } catch {
                    // Best-effort cleanup only - the write already failed, and a leftover uniquely-named
                    // temp file here is harmless clutter, not a correctness problem (unlike the old fixed
                    // ".tmp" name, it can never be mistaken for a later call's own temp file).
                }
                return false;
            }
        }
    }
}
