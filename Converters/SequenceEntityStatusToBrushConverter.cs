using NINA.Core.Enum;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JeffRidder.NINA.Nightfront.Converters {

    /// <summary>Maps a sequence item's live SequenceEntityStatus to a status-dot brush.</summary>
    public class SequenceEntityStatusToBrushConverter : IValueConverter {
        private static readonly SolidColorBrush PendingBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush RunningBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        private static readonly SolidColorBrush FinishedBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush FailedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly SolidColorBrush SkippedBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is SequenceEntityStatus status) {
                switch (status) {
                    case SequenceEntityStatus.RUNNING: return RunningBrush;
                    case SequenceEntityStatus.FINISHED: return FinishedBrush;
                    case SequenceEntityStatus.FAILED: return FailedBrush;
                    case SequenceEntityStatus.SKIPPED:
                    case SequenceEntityStatus.DISABLED: return SkippedBrush;
                    default: return PendingBrush;
                }
            }
            return PendingBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
