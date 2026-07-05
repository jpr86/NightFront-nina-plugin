using System;
using System.Globalization;
using System.Windows.Data;

namespace JeffRidder.NINA.Nightfront.Converters {

    /// <summary>
    /// Formats a NightFrontTargetSummary's WindowStart/WindowEnd (bound as a two-value MultiBinding)
    /// into "9:35 PM – 1:25 AM" display text. WindowStart is null for the night's first target,
    /// since a target's own imported instructions only carry its end-of-window boundary - the first
    /// target's real start isn't known until the sequence actually begins.
    /// </summary>
    public class TargetTimeWindowConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            var start = values.Length > 0 && values[0] is DateTime s ? s.ToString("h:mm tt", culture) : "—";
            var end = values.Length > 1 && values[1] is DateTime e ? e.ToString("h:mm tt", culture) : "—";
            return $"{start} – {end}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
