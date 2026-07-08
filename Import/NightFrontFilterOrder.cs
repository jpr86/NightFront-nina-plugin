using System.Collections.Generic;
using System.Linq;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Parses the plugin's "flat filter order" option (a comma-separated list of filter names, e.g.
    /// "L, B, G, R, OIII, Ha, SII") into the ordered list NightFrontMetadataStore.SelectNext ranks
    /// outstanding calibration requirements by.
    /// </summary>
    public static class NightFrontFilterOrder {
        public static List<string> Parse(string raw) {
            if (string.IsNullOrWhiteSpace(raw)) {
                return new List<string>();
            }

            return raw.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
