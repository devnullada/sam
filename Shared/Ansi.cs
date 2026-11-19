using System.Text.RegularExpressions;

namespace ServiceManager.Shared;

public static partial class Ansi
{
    // CSI sequences (ESC [ ... final byte) and OSC sequences (ESC ] ... BEL or ST)
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\].*?(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex AllRegex();

    // Everything except SGR (color/style) sequences, which end in 'm'
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-ln-~]|\x1B\].*?(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex NonSgrRegex();

    /// <summary>Removes all ANSI escape sequences.</summary>
    public static string Strip(string text) => AllRegex().Replace(text, "");

    /// <summary>Removes ANSI escape sequences but keeps SGR color/style codes.</summary>
    public static string StripNonSgr(string text) => NonSgrRegex().Replace(text, "");
}
