using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

/// <summary>
/// Замена <c>DataTrigger</c>, которого в WinUI нет. Значения задаются при объявлении ресурса,
/// поэтому один конвертер обслуживает разные пары значений.
/// </summary>
public sealed class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public HorizontalAlignment TrueValue { get; set; } = HorizontalAlignment.Right;

    public HorizontalAlignment FalseValue { get; set; } = HorizontalAlignment.Left;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueValue { get; set; }

    public Brush? FalseValue { get; set; }

    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BoolToThicknessConverter : IValueConverter
{
    public Thickness TrueValue { get; set; }

    public Thickness FalseValue { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
