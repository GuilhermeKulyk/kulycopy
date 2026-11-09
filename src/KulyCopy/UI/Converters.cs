using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KulyCopy.UI;

/// <summary>bool → Visibility; com ConverterParameter="Invert" inverte.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>string vazia/nula → Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = string.IsNullOrEmpty(value as string);
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase)) empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null → Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>int? ↔ string para caixas de texto numéricas.</summary>
public sealed class NullableIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? i.ToString(CultureInfo.InvariantCulture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return null;
    }
}
