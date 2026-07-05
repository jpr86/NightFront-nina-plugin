using NINA.Astrometry;
using System;
using System.Globalization;
using System.Windows.Data;

namespace JeffRidder.NINA.Nightfront.Converters {

    /// <summary>
    /// Formats an InputCoordinates' RA/Dec into display text, preferring NINA's own
    /// InputCoordinates.Coordinates.RAString/DecString (e.g. "20:59:17" / "44° 31' 44\"") and only
    /// falling back to hand-formatting the raw components if Coordinates is unavailable.
    /// </summary>
    public class InputCoordinatesToRaDecTextConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not InputCoordinates coordinates) {
                return string.Empty;
            }

            var resolved = coordinates.Coordinates;
            if (resolved != null) {
                return $"{resolved.RAString} / {resolved.DecString}";
            }

            var ra = $"{coordinates.RAHours}h{coordinates.RAMinutes:00}m{coordinates.RASeconds:00.#}s";
            var decSign = coordinates.NegativeDec ? "-" : "+";
            var dec = $"{decSign}{coordinates.DecDegrees}°{coordinates.DecMinutes:00}'{coordinates.DecSeconds:00.#}\"";
            return $"{ra} / {dec}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
