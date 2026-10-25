using System;
using System.Globalization;
using System.Windows.Data;

namespace VlcControl
{
    public sealed class FillWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return 0d;

            var value = ToDouble(values[0]);
            var max = ToDouble(values[1]);
            var width = ToDouble(values[2]);

            if (max <= 0 || width <= 0) return 0d;
            return Math.Max(0, Math.Min(width, value / max * width));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static double ToDouble(object value)
        {
            try
            {
                return value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }
    }
}
