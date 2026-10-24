using System.Globalization;
using System.Windows.Data;

namespace Icono.App.Converters;

/// <summary>Shows <c>Shape.Name</c> when the user has typed one, falling back to the auto-generated
/// "{Kind} {index}" label (<see cref="Icono.App.Views.ShapeListItem.DisplayName"/>) otherwise.
/// Typing into the bound TextBox always writes straight back to <c>Shape.Name</c> — clearing the
/// box reverts to the auto label rather than leaving it blank.</summary>
public sealed class ShapeNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = values.Length > 0 ? values[0] as string : null;
        var fallback = values.Length > 1 ? values[1] as string : null;
        return string.IsNullOrWhiteSpace(name) ? fallback ?? string.Empty : name;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [value as string ?? string.Empty, Binding.DoNothing];
}
