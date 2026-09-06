using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GogDisc.Launcher;

/// <summary>Builds a pill-shaped clip that tracks an element's size. ClipToBounds only clips the bounding
/// rectangle, so without this a child painted at the edge shows square corners past a rounded container.</summary>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double width || values[1] is not double height) return null;
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return null;
        var radius = height / 2;
        return new RectangleGeometry(new System.Windows.Rect(0, 0, width, height), radius, radius);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
