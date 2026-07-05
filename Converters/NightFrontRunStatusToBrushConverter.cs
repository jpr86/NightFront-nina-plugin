using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JeffRidder.NINA.Nightfront.Converters {

    /// <summary>
    /// Maps NightFrontUpdateInstruction.LastRunSucceeded (null = never run, true = success, false =
    /// failure) to a status-dot brush for the sequencer UI.
    /// </summary>
    public class NightFrontRunStatusToBrushConverter : IValueConverter {
        private static readonly SolidColorBrush NeverRunBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush FailureBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is bool success) {
                return success ? SuccessBrush : FailureBrush;
            }
            return NeverRunBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
