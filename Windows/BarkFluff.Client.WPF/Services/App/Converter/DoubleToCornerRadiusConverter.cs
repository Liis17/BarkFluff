using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BarkFluff.Client.WPF.Services.App.Converter
{
    /// <summary>
    /// Конвертирует double (значение слайдера) в CornerRadius для превью пузырьков сообщений.
    /// </summary>
    public class DoubleToCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double r = value is double d ? d : 0;
            return new CornerRadius(r);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
