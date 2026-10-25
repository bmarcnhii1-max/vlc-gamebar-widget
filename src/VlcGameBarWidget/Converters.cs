using System;
using Windows.UI;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace VlcGameBarWidget
{
    public sealed class CurrentBarBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Current = new SolidColorBrush(Color.FromArgb(255, 255, 138, 0));
        private static readonly SolidColorBrush Normal = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool isCurrent && isCurrent ? Current : Normal;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed class CurrentTextBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Current = new SolidColorBrush(Color.FromArgb(255, 255, 176, 74));
        private static readonly SolidColorBrush Normal = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));

        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool isCurrent && isCurrent ? Current : Normal;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
