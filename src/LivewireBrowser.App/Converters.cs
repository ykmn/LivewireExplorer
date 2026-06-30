using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LivewireBrowser.App;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>
/// Renders a ComboBox's closed-box selection text the same way DisplayMemberPath renders
/// dropdown items. Confirmed empirically (a minimal repro app, no custom styling at all)
/// that ComboBox.SelectionBoxItem holds the raw selected object — not the DisplayMemberPath
/// value — and SelectionBoxItemTemplate stays null, so the usual
/// "TemplateBinding SelectionBoxItem/SelectionBoxItemTemplate" recipe many ComboBox retemplate
/// guides use does not work as documented on .NET 8. This reflects the (single-level)
/// DisplayMemberPath property off the selected item directly instead; falls back to
/// value.ToString() when DisplayMemberPath is empty (e.g. a plain enum ComboBox).
/// </summary>
public class DisplayMemberPathConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not { } item)
            return string.Empty;

        var path = values[1] as string;
        if (string.IsNullOrEmpty(path))
            return item.ToString() ?? string.Empty;

        var property = item.GetType().GetProperty(path);
        return property?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Turns a 0..1 fraction into a Star GridLength, used directly to size the stacked
/// RowDefinitions of the segmented meter bars in MainWindow.xaml (each meter binds 6 rows —
/// red/yellow/green, unlit/lit — straight to PlayerViewModel's MeterZoneFractions properties,
/// which already sum to 1.0). Originally built for ProgressBar's own Value, hence the
/// "Empty" parameter that returns the inverse (1 - value) — no longer used now that the
/// meters compute each row's fraction themselves, but harmless to keep for any future
/// single-bar use. ProgressBar's built-in PART_Track/PART_Indicator part-name convention
/// (what the default theme uses to auto-size its indicator) was confirmed via an isolated
/// repro app to leave PART_Indicator at 0 height for Orientation="Vertical" in a custom
/// template — this Star-row approach is what replaced it.
/// </summary>
public class ProgressFillConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is double d ? Math.Clamp(d, 0.0, 1.0) : 0.0;
        if (parameter as string == "Empty")
            v = 1.0 - v;

        return new GridLength(Math.Max(v, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
