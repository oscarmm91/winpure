using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinPure.Views;

/// <summary>
/// True when the bound value equals the ConverterParameter — used to show which preset is
/// active. Without it the preset radio buttons only looked selected until the user navigated
/// away, because each page builds its own set from the DataTemplate.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    // The click itself sets the button; this binding only reflects state back.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// True only when every bound value is true. A tweak's toggle is enabled while the app is idle AND the tweak can still
/// be switched — an app removal that is already done cannot be switched back off.
/// </summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
