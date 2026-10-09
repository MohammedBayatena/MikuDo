using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MikuDo.Controls;

/// <summary>
/// How the pages are laid out. Islands sets each list, table and panel on the
/// canvas as a card of its own; Flat runs them edge to edge as areas divided by
/// borders, with only the floating things (dialogs, menus) left raised. Styles
/// watch <see cref="IsFlat"/>, so a change applies to everything on screen.
/// </summary>
public sealed partial class Layout : ObservableObject
{
    public static Layout Current { get; } = new();

    public const string Islands = "Islands";
    public const string Flat = "Flat";

    [ObservableProperty] private bool _isFlat;

    public string Id => IsFlat ? Flat : Islands;

    partial void OnIsFlatChanged(bool value) => OnPropertyChanged(nameof(Id));
}

/// <summary>
/// One value in the Islands layout and another in Flat:
/// <c>Margin="{ui:ByLayout Islands='6,0', Flat=0}"</c>. For sizes, spacing and
/// visibility; a colour changes through a style trigger instead, so it still
/// follows the theme.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class ByLayoutExtension : MarkupExtension
{
    public object? Islands { get; set; }
    public object? Flat { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(Layout.IsFlat))
        {
            Source = Layout.Current,
            Mode = BindingMode.OneWay,
            Converter = new Pick(Islands, Flat)
        };
        return binding.ProvideValue(serviceProvider);
    }

    /// <summary>Turns the chosen value, often written as text, into what the property takes.</summary>
    private sealed class Pick : IValueConverter
    {
        private readonly object? _islands;
        private readonly object? _flat;
        private readonly Dictionary<Type, (object? Islands, object? Flat)> _converted = new();

        public Pick(object? islands, object? flat)
        {
            _islands = islands;
            _flat = flat;
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!_converted.TryGetValue(targetType, out var pair))
                _converted[targetType] = pair = (To(_islands, targetType), To(_flat, targetType));
            return value is true ? pair.Flat : pair.Islands;
        }

        private static object? To(object? value, Type targetType)
        {
            if (value == null || targetType.IsInstanceOfType(value)) return value;
            if (value is string text)
            {
                if (targetType == typeof(object)) return text;
                return TypeDescriptor.GetConverter(targetType).ConvertFromInvariantString(text);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
