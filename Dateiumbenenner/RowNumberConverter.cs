using System;
using System.Globalization;
using System.Windows.Data;

namespace Dateiumbenenner
{
    /// <summary>Zeilennummer (1-basiert) aus ItemsControl.AlternationIndex.</summary>
    public sealed class RowNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? (i + 1).ToString(culture) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
