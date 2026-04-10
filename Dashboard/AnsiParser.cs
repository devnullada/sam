using System.Text.RegularExpressions;
using System.Windows.Media;

namespace ServiceManager.Dashboard;

public static partial class AnsiParser
{
    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush DefaultBrush = Frozen("#cccccc");

    private static readonly Dictionary<int, Brush> StandardColors = new()
    {
        [30] = Brushes.Black,
        [31] = Frozen("#cd3131"),
        [32] = Frozen("#0dbc79"),
        [33] = Frozen("#e5e510"),
        [34] = Frozen("#2472c8"),
        [35] = Frozen("#bc3fbc"),
        [36] = Frozen("#11a8cd"),
        [37] = Frozen("#e5e5e5"),
    };

    private static readonly Dictionary<int, Brush> BrightColors = new()
    {
        [90] = Frozen("#666666"),
        [91] = Frozen("#f14c4c"),
        [92] = Frozen("#23d18b"),
        [93] = Frozen("#f5f543"),
        [94] = Frozen("#3b8eea"),
        [95] = Frozen("#d670d6"),
        [96] = Frozen("#29b8db"),
        [97] = Brushes.White,
    };

    [GeneratedRegex(@"\x1b\[([0-9;]*)m")]
    private static partial Regex AnsiEscapeRegex();

    public record TextSegment(string Text, Brush Foreground);

    public static List<TextSegment> Parse(string input)
    {
        var segments = new List<TextSegment>();
        var currentBrush = DefaultBrush;
        var lastIndex = 0;

        foreach (Match match in AnsiEscapeRegex().Matches(input))
        {
            if (match.Index > lastIndex)
                segments.Add(new TextSegment(input[lastIndex..match.Index], currentBrush));

            var codes = match.Groups[1].Value;
            if (string.IsNullOrEmpty(codes) || codes == "0")
            {
                currentBrush = DefaultBrush;
            }
            else
            {
                foreach (var part in codes.Split(';'))
                {
                    if (int.TryParse(part, out var code))
                    {
                        if (code == 0) currentBrush = DefaultBrush;
                        else if (code == 1) { } // bold — ignored for now
                        else if (StandardColors.TryGetValue(code, out var std)) currentBrush = std;
                        else if (BrightColors.TryGetValue(code, out var bright)) currentBrush = bright;
                    }
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < input.Length)
            segments.Add(new TextSegment(input[lastIndex..], currentBrush));

        return segments;
    }
}
