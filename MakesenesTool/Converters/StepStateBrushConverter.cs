using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MakesenesTool.Models;

namespace MakesenesTool.Converters;

public class StepStateBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is StepState s ? s : StepState.Future;
        var part = parameter?.ToString();

        if (part == "LabelBackground" && state != StepState.Current)
            return System.Windows.Media.Brushes.Transparent;

        var key = (part, state) switch
        {
            ("Circle", StepState.Future) => "CardBrush",
            ("Circle", _) => "AccentBrush",
            ("CircleBorder", StepState.Future) => "MutedBrush",
            ("CircleBorder", _) => "AccentBrush",
            ("CircleText", StepState.Future) => "MutedBrush",
            ("CircleText", _) => "CardBrush",
            ("LabelBackground", _) => "AccentBrush",
            ("LabelText", StepState.Current) => "CardBrush",
            ("LabelText", StepState.Future) => "MutedBrush",
            _ => "TextBrush",
        };
        return System.Windows.Application.Current.FindResource(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
