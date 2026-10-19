using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BIMDrawingFactory.UI.Controls
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isTrue = value is bool && (bool)value;
            if (Invert) isTrue = !isTrue;
            return isTrue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isVisible = value is Visibility && (Visibility)value == Visibility.Visible;
            return Invert ? !isVisible : isVisible;
        }
    }

    public static class VisibilityConverters
    {
        private static readonly BoolToVisibilityConverter _boolToVisibilityConverter = new BoolToVisibilityConverter { Invert = false };
        private static readonly BoolToVisibilityConverter _inverseBoolToVisibilityConverter = new BoolToVisibilityConverter { Invert = true };

        public static BoolToVisibilityConverter BoolToVisibilityConverter
        {
            get { return _boolToVisibilityConverter; }
        }

        public static BoolToVisibilityConverter InverseBoolToVisibilityConverter
        {
            get { return _inverseBoolToVisibilityConverter; }
        }
    }
}
