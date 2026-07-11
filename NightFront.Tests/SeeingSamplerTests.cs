using System;
using System.IO;
using JeffRidder.NINA.Nightfront.Sequencer;
using Xunit;

namespace JeffRidder.NINA.Nightfront.Tests {

    public class SeeingSamplerTests {

        private static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private static string TessdataDir => Path.Combine(AppContext.BaseDirectory, "tessdata");

        // ── OcrImageBytes against real captured images ──────────────────────────────────────────
        // Ground truth verified by eye against the real telemetry-page images this fixture was
        // captured from (hcronewmexico.com/telemetry/'s Alcor seeing monitor): "Zenith 1.61",
        // r0 = 70.3 mm".

        [Fact]
        public void OcrImageBytes_CurrentConditionFixture_ReadsTheCorrectFwhm() {
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDir, "LastCurrentSeeing.jpg"));

            var sample = SeeingSampler.OcrImageBytes(bytes, TessdataDir);

            Assert.True(sample.Success);
            Assert.Equal(1.61, sample.FwhmArcsec!.Value, 2);
        }

        [Fact]
        public void OcrImageBytes_CurrentConditionFixture_ReadsTheCorrectR0() {
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDir, "LastCurrentSeeing.jpg"));

            var sample = SeeingSampler.OcrImageBytes(bytes, TessdataDir);

            Assert.Equal(70.3, sample.R0Mm!.Value, 1);
        }

        [Fact]
        public void OcrImageBytes_CurrentConditionFixture_ReadsAParseableOnImageTimestamp() {
            // Regression coverage for the malformed-seconds-colon gap ("5:0414 AM" OCR'd from
            // "5:04:14 AM") that DateTime.TryParse used to silently swallow.
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDir, "LastCurrentSeeing.jpg"));

            var sample = SeeingSampler.OcrImageBytes(bytes, TessdataDir);

            Assert.NotNull(sample.ReportedAtLocal);
        }

        [Fact]
        public void OcrImageBytes_NightlyMeanFixture_ReadsTheCorrectFwhm() {
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDir, "MeanCurrentSeeing.jpg"));

            var sample = SeeingSampler.OcrImageBytes(bytes, TessdataDir);

            Assert.True(sample.Success);
            Assert.Equal(1.59, sample.FwhmArcsec!.Value, 2);
        }

        [Fact]
        public void OcrImageBytes_GarbageImage_FailsWithoutThrowing() {
            var bytes = File.ReadAllBytes(Path.Combine(FixturesDir, "Garbage.jpg"));

            var sample = SeeingSampler.OcrImageBytes(bytes, TessdataDir);

            Assert.False(sample.Success);
            Assert.Null(sample.FwhmArcsec);
            Assert.NotNull(sample.Error);
        }

        // ── ResolveImageUrlFromHtml ──────────────────────────────────────────────────────────────

        [Fact]
        public void ResolveImageUrlFromHtml_PrefersCurrentConditionOverNightlyMean() {
            var html = @"
                <img src=""/alcor/MeanCurrentSeeing.jpg"" />
                <img src=""/alcor/LastCurrentSeeing.jpg"" />
                <img src=""/allskeye/currentimage.jpg"" />";

            var result = SeeingSampler.ResolveImageUrlFromHtml(html, new Uri("https://example.com/telemetry/"));

            Assert.Equal("https://example.com/alcor/LastCurrentSeeing.jpg", result.ToString());
        }

        [Fact]
        public void ResolveImageUrlFromHtml_ResolvesRelativeUrlsAgainstTheBase() {
            var html = @"<img src=""alcor/LastCurrentSeeing.jpg"" />";

            var result = SeeingSampler.ResolveImageUrlFromHtml(html, new Uri("https://hcronewmexico.com/telemetry/"));

            Assert.Equal("https://hcronewmexico.com/telemetry/alcor/LastCurrentSeeing.jpg", result.ToString());
        }

        [Fact]
        public void ResolveImageUrlFromHtml_NoSeeingImage_Throws() {
            var html = @"<img src=""/allskeye/currentimage.jpg"" /><img src=""/skyalert/weathergraph.png"" />";

            Assert.Throws<InvalidOperationException>(() =>
                SeeingSampler.ResolveImageUrlFromHtml(html, new Uri("https://example.com/telemetry/")));
        }

        // ── IsStale ───────────────────────────────────────────────────────────────────────────

        [Fact]
        public void IsStale_NoTimestamp_IsNeverStale() {
            var sample = new SeeingSample(true, 1.5, null, null, "text", null);

            Assert.False(SeeingSampler.IsStale(sample, DateTime.Now, TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void IsStale_WithinMaxAge_IsNotStale() {
            var now = new DateTime(2026, 7, 11, 5, 10, 0);
            var sample = new SeeingSample(true, 1.5, null, now.AddMinutes(-5), "text", null);

            Assert.False(SeeingSampler.IsStale(sample, now, TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void IsStale_OlderThanMaxAge_IsStale() {
            var now = new DateTime(2026, 7, 11, 5, 30, 0);
            var sample = new SeeingSample(true, 1.5, null, now.AddMinutes(-20), "text", null);

            Assert.True(SeeingSampler.IsStale(sample, now, TimeSpan.FromMinutes(10)));
        }
    }
}
