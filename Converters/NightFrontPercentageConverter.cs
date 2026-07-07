using System;
using System.Globalization;
using System.Windows.Data;

namespace JeffRidder.NINA.Nightfront.Converters {

    /// <summary>
    /// Displays a fractional 0-1 property (e.g. HistogramTargetPercentage = 0.4) as a whole
    /// percentage (40) in a UnitTextBox with a "%" unit, and converts typed input back to the
    /// fractional form the underlying NINA SkyFlat property expects.
    /// </summary>
    public class NightFrontPercentageConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is double d ? d * 100.0 : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is double d) {
                return d / 100.0;
            }
            if (value is string s && double.TryParse(s, NumberStyles.Any, culture, out var parsed)) {
                return parsed / 100.0;
            }
            return value;
        }
    }
}
