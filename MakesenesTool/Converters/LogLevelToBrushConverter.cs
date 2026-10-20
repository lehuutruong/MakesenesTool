using MakesenesTool.Models;
using System.Globalization;
using System.Windows.Data;

namespace MakesenesTool.Converters;

public class LogLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Ok => "OkBrush",
            LogLevel.Warning => "WarningBrush",
            LogLevel.Error => "ErrorBrush",
            _ => "TextBrush",
        };
        return System.Windows.Application.Current.FindResource(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
