using System.Text.RegularExpressions;
using System.Windows.Media;

namespace ServiceManager.Dashboard;

public static partial class AnsiParser
{
    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(string hex) => Frozen((Color)ColorConverter.ConvertFromString(hex));

    private static readonly Brush DefaultBrush = Frozen("#cccccc");

    // 16-color palette: indices 0-7 standard, 8-15 bright
    private static readonly Brush[] Palette16 =
    [
        Frozen("#000000"), Frozen("#cd3131"), Frozen("#0dbc79"), Frozen("#e5e510"),
        Frozen("#2472c8"), Frozen("#bc3fbc"), Frozen("#11a8cd"), Frozen("#e5e5e5"),
        Frozen("#666666"), Frozen("#f14c4c"), Frozen("#23d18b"), Frozen("#f5f543"),
        Frozen("#3b8eea"), Frozen("#d670d6"), Frozen("#29b8db"), Frozen("#ffffff"),
    ];

    private static readonly Brush[] Palette256 = BuildPalette256();

    private static Brush[] BuildPalette256()
    {
        var p = new Brush[256];
        for (var i = 0; i < 16; i++) p[i] = Palette16[i];
        byte[] levels = [0, 95, 135, 175, 215, 255];
        for (var i = 0; i < 216; i++)
            p[16 + i] = Frozen(Color.FromRgb(levels[i / 36], levels[i / 6 % 6], levels[i % 6]));
        for (var i = 0; i < 24; i++)
        {
            var v = (byte)(8 + i * 10);
            p[232 + i] = Frozen(Color.FromRgb(v, v, v));
        }
        return p;
    }

    // SGR sequences are captured; any other escape sequence is matched and dropped
    [GeneratedRegex(@"\x1b\[([0-9;:]*)m|\x1B\[[0-?]*[ -/]*[@-~]|\x1B\].*?(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex AnsiEscapeRegex();

    public record TextSegment(string Text, Brush Foreground, Brush? Background, bool Bold, bool Italic, bool Underline);

    private struct Style
    {
        public Brush? Fg, Bg;
        public bool Bold, Italic, Underline, Inverse;
    }

    public static List<TextSegment> Parse(string input)
    {
        var segments = new List<TextSegment>();
        var style = new Style();
        var lastIndex = 0;

        void Emit(string text)
        {
            var fg = style.Fg ?? DefaultBrush;
            var bg = style.Bg;
            if (style.Inverse) (fg, bg) = (bg ?? Brushes.Black, fg);
            segments.Add(new TextSegment(text, fg, bg, style.Bold, style.Italic, style.Underline));
        }

        foreach (Match match in AnsiEscapeRegex().Matches(input))
        {
            if (match.Index > lastIndex)
                Emit(input[lastIndex..match.Index]);
            lastIndex = match.Index + match.Length;

            if (!match.Groups[1].Success) continue; // non-SGR sequence, drop it

            var parts = match.Groups[1].Value.Split(';', ':');
            var codes = parts.Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            if (codes.Length == 0) codes = [0];

            for (var i = 0; i < codes.Length; i++)
            {
                var code = codes[i];
                switch (code)
                {
                    case 0: style = new Style(); break;
                    case 1: style.Bold = true; break;
                    case 3: style.Italic = true; break;
                    case 4: style.Underline = true; break;
                    case 7: style.Inverse = true; break;
                    case 22: style.Bold = false; break;
                    case 23: style.Italic = false; break;
                    case 24: style.Underline = false; break;
                    case 27: style.Inverse = false; break;
                    case >= 30 and <= 37: style.Fg = Palette16[code - 30]; break;
                    case 39: style.Fg = null; break;
                    case >= 40 and <= 47: style.Bg = Palette16[code - 40]; break;
                    case 49: style.Bg = null; break;
                    case >= 90 and <= 97: style.Fg = Palette16[code - 90 + 8]; break;
                    case >= 100 and <= 107: style.Bg = Palette16[code - 100 + 8]; break;
                    case 38 or 48:
                        var brush = ReadExtendedColor(codes, ref i);
                        if (brush is not null)
                        {
                            if (code == 38) style.Fg = brush; else style.Bg = brush;
                        }
                        break;
                }
            }
        }

        if (lastIndex < input.Length)
            Emit(input[lastIndex..]);

        return segments;
    }

    // Handles 38;5;n / 48;5;n (256-color) and 38;2;r;g;b / 48;2;r;g;b (truecolor)
    private static Brush? ReadExtendedColor(int[] codes, ref int i)
    {
        if (i + 1 >= codes.Length) return null;
        if (codes[i + 1] == 5 && i + 2 < codes.Length)
        {
            var n = codes[i + 2];
            i += 2;
            return n is >= 0 and < 256 ? Palette256[n] : null;
        }
        if (codes[i + 1] == 2 && i + 4 < codes.Length)
        {
            var color = Color.FromRgb((byte)Math.Clamp(codes[i + 2], 0, 255), (byte)Math.Clamp(codes[i + 3], 0, 255), (byte)Math.Clamp(codes[i + 4], 0, 255));
            i += 4;
            return Frozen(color);
        }
        return null;
    }
}
