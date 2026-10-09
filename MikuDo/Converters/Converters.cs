using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MikuDo.Models;

namespace MikuDo.Converters;

/// <summary>bool / int / string / object → Visibility. Pass "invert" to flip.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            bool b => b,
            int i => i > 0,
            double d => d > 0,
            string s => s.Length > 0,
            null => false,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
        if (parameter?.ToString() == "invert") truthy = !truthy;
        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>true → "on", false → "off". Drives the selected look of Tag-based styles.</summary>
public class BoolToOnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value is bool b && b;
        if (parameter?.ToString() == "invert") on = !on;
        return on ? "on" : "off";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"on" when the bound value equals the converter parameter.</summary>
public class EqualsToOnOffConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? "on" : "off";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Multiplies a 0..1 fraction by the parameter. Used for chart and waveform heights.</summary>
/// <summary>
/// A share of the room there is: the first value is a fraction, the second the
/// height available, the parameter the pixels kept back for labels.
/// </summary>
public class ShareOfConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = values.Length > 0 && values[0] is double f ? f : 0;
        var room = values.Length > 1 && values[1] is double h ? h : 0;
        var kept = double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var k) ? k : 0;
        return Math.Max(0, fraction * (room - kept));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0;
        var scale = double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 1;
        return Math.Max(0, fraction * scale);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Rotates the list-view section chevron: 90° when open, 0° when collapsed.</summary>
public class ExpandedToAngleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? 90.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Strikes through the title of a finished task.</summary>
public class DoneToDecorationsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TextDecorations.Strikethrough : null!;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DoneToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Application.Current.FindResource(value is bool b && b ? "TextFaintBrush" : "TextPrimaryBrush");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Packs a command parameter and a constant into one array for multi-argument commands.</summary>
public class PairConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.ToArray();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Collapses a grid column to zero width when the bound flag is false.</summary>
public class BoolToStarWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The width of a panel's column. The parameter is "open|collapsed" in pixels
/// (a comma would end the markup extension's argument);
/// a collapsed panel keeps a narrow rail rather than disappearing, so it is
/// still obvious it is there.
/// </summary>
public class CollapsedToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = parameter?.ToString()?.Split('|') ?? Array.Empty<string>();
        var wanted = value is bool collapsed && collapsed ? 1 : 0;

        if (parts.Length > wanted &&
            double.TryParse(parts[wanted], NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
            return new GridLength(width);

        return new GridLength(wanted == 1 ? 0 : 240);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"on" when the two bound values are equal. Used to light up a chip in a list.</summary>
public class MultiEqualsToOnOffConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && Equals(values[0], values[1]) ? "on" : "off";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible only when every bound flag is false.</summary>
public class ShowIfNeitherConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Any(v => v is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible only when every bound flag is true.</summary>
public class ShowIfBothConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>A priority's text colour, for pickers that list every priority.</summary>
public class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Palette.Priority(value is TodoPriority p ? p : TodoPriority.None).Foreground;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"High", "Medium", "Low" or "No priority".</summary>
public class PriorityToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ViewModels.TaskCardViewModel.PriorityText(value is TodoPriority p ? p : TodoPriority.None);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Flips a flag, for conditions that need its opposite.</summary>
public class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// Open only in the view on screen: true when the flag is set and the board's
/// view mode matches the view this copy belongs to.
/// </summary>
/// <remarks>
/// A list's tools appear in both the kanban and the list view, and the view
/// not in use is collapsed rather than removed. A popup opens whether or not
/// its anchor is showing, so two copies bound to one flag both open, and the
/// two then fight over the mouse capture that closes them on an outside click.
/// Closing writes the flag back; the view and owner are never written.
/// </remarks>
public class OpenInViewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 3 && values[0] is true && values[1] is string view && values[2] is string owner
           && string.Equals(view, owner, StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => new[] { value, Binding.DoNothing, Binding.DoNothing };
}

/// <summary>
/// The sidebar column: the parameter's width in pixels while collapsed to its
/// rail, the user's chosen width while open.
/// </summary>
public class SidebarColumnConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var collapsed = values.Length > 0 && values[0] is true;
        var rail = double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 48;
        var open = values.Length > 1 && values[1] is double w ? w : 268;
        return new GridLength(collapsed ? rail : open);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The bound number less the parameter, never below zero: room left after fixed parts.</summary>
public class SubtractConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var by = double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return value is double d ? Math.Max(0, d - by) : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible while the bound width reaches the parameter: a label that gives way when space runs short.</summary>
public class WidthAtLeastConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var least = double.TryParse(parameter?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return value is double width && width >= least ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
