using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;

namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// Result of one attempt to sample a remote "current condition" seeing reading.
    /// </summary>
    public sealed record SeeingSample(
        bool Success,
        double? FwhmArcsec,
        double? R0Mm,
        DateTime? ReportedAtLocal,
        string RawOcrText,
        string Error);

    /// <summary>
    /// Fetch step a NightFrontSeeingTrigger runs on each poll: resolve a seeing-monitor image from
    /// a configured data-source URL (either the image itself, or an HTML page it's embedded in),
    /// download it, and OCR the rendered numeric readout out of it. Verified against the real
    /// hcronewmexico.com/telemetry/ Alcor seeing-monitor image by prototype before being built out -
    /// the source images are small, high-contrast rendered-text panels, not photos, so Tesseract
    /// reads them reliably with no preprocessing.
    /// </summary>
    public static class SeeingSampler {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // Matches "Zenith" ... "1.61" ... a trailing inch mark (straight or curly quote, OCR is inconsistent about it).
        private static readonly Regex FwhmPattern = new Regex(@"(\d+\.\d+)\s*[""'”″]", RegexOptions.Compiled);

        private static readonly Regex R0Pattern = new Regex(@"r0\s*=\s*(\d+\.?\d*)\s*mm", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Tesseract occasionally drops the seconds' colon on this tiny font (e.g. "5:0414 AM"
        // instead of "5:04:14 AM"). DateTime.TryParse rejects that malformed form outright, so the
        // H/M/S digit groups are captured individually and reassembled instead of parsing the
        // whole matched string as one datetime literal.
        private static readonly Regex TimestampPattern = new Regex(
            @"(\d{1,2})/(\d{1,2})/(\d{4})\s+(\d{1,2}):(\d{2}):?(\d{2})\s*([AP]M)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Given a configured data-source URL (telemetry page OR a direct image URL), resolves the
        /// actual "current condition" seeing image URL to fetch. Not hardcoded to HCRO specifically
        /// - any Alcor-based telemetry page publishing a "Current Condition"/"Nightly Mean" image
        /// pair resolves via the same &lt;img&gt;-src heuristic.
        /// </summary>
        public static async Task<Uri> ResolveImageUrlAsync(Uri dataSourceUrl) {
            var head = await Http.GetAsync(dataSourceUrl).ConfigureAwait(false);
            head.EnsureSuccessStatusCode();

            var contentType = head.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) {
                // Option already points straight at the image.
                return dataSourceUrl;
            }

            var html = await head.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ResolveImageUrlFromHtml(html, dataSourceUrl);
        }

        /// <summary>
        /// Pure HTML-parsing half of ResolveImageUrlAsync, split out for unit testing without a
        /// live network call. Prefers an &lt;img&gt; src containing "current"/"last" over
        /// "mean"/"nightly" so the instantaneous reading wins over a night's running average when a
        /// page publishes both from the same Alcor seeing-monitor software.
        /// </summary>
        public static Uri ResolveImageUrlFromHtml(string html, Uri baseUrl) {
            var imgSrcs = Regex.Matches(html, @"<img[^>]+src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .ToList();

            var candidates = imgSrcs.Where(src => src.Contains("seeing", StringComparison.OrdinalIgnoreCase)).ToList();

            var best = candidates
                .OrderByDescending(src => src.Contains("current", StringComparison.OrdinalIgnoreCase)
                                           || src.Contains("last", StringComparison.OrdinalIgnoreCase))
                .ThenBy(src => src.Contains("mean", StringComparison.OrdinalIgnoreCase)
                               || src.Contains("nightly", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (best is null) {
                throw new InvalidOperationException(
                    $"No <img> src on {baseUrl} looked like a seeing-monitor image (checked {imgSrcs.Count} images).");
            }

            return new Uri(baseUrl, best);
        }

        public static async Task<SeeingSample> SampleAsync(Uri dataSourceUrl, string tessdataDir) {
            try {
                var imageUrl = await ResolveImageUrlAsync(dataSourceUrl).ConfigureAwait(false);
                var bytes = await Http.GetByteArrayAsync(imageUrl).ConfigureAwait(false);
                return OcrImageBytes(bytes, tessdataDir);
            } catch (Exception ex) {
                return new SeeingSample(false, null, null, null, "", ex.Message);
            }
        }

        public static SeeingSample OcrImageBytes(byte[] imageBytes, string tessdataDir) {
            using var engine = new TesseractEngine(tessdataDir, "eng", EngineMode.Default);
            engine.SetVariable("tessedit_pageseg_mode", "3");
            using var img = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(img);
            var text = page.GetText();

            var fwhmMatch = FwhmPattern.Match(text);
            var r0Match = R0Pattern.Match(text);
            var tsMatch = TimestampPattern.Match(text);

            double? fwhm = fwhmMatch.Success ? double.Parse(fwhmMatch.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            double? r0 = r0Match.Success ? double.Parse(r0Match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            DateTime? ts = ParseTimestamp(tsMatch);

            return new SeeingSample(
                Success: fwhm.HasValue,
                FwhmArcsec: fwhm,
                R0Mm: r0,
                ReportedAtLocal: ts,
                RawOcrText: text,
                Error: fwhm.HasValue ? null : "Could not find a \"<number> arcsec-mark\" pattern in OCR text.");
        }

        private static DateTime? ParseTimestamp(Match m) {
            if (!m.Success) {
                return null;
            }
            try {
                int month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                int minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                int second = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
                bool pm = m.Groups[7].Value.Equals("PM", StringComparison.OrdinalIgnoreCase);
                if (hour == 12) {
                    hour = 0;
                }
                if (pm) {
                    hour += 12;
                }
                return new DateTime(year, month, day, hour, minute, second);
            } catch (Exception) {
                // Out-of-range digit groups (e.g. a badly misread month/day) - treat as unparsed
                // rather than throwing out of a best-effort OCR extraction.
                return null;
            }
        }

        /// <summary>
        /// True if a sample's on-image timestamp is old enough that it should be treated as
        /// untrustworthy (e.g. a CDN- or webserver-cached stale JPEG read with valid-looking but
        /// outdated digits) rather than acted on as current. A sample with no parseable timestamp
        /// is not considered stale by this check alone - the caller decides whether that's
        /// acceptable for its data source.
        /// </summary>
        public static bool IsStale(SeeingSample sample, DateTime nowLocal, TimeSpan maxAge) {
            if (sample.ReportedAtLocal is not DateTime reportedAt) {
                return false;
            }
            return (nowLocal - reportedAt) > maxAge;
        }
    }
}
