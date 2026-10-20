using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MakesenesTool.Converters;

public class EnumMatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name)
            return Enum.Parse(targetType, name);
        return System.Windows.Data.Binding.DoNothing;
    }
}
