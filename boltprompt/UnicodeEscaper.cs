using System.Text;
using System.Text.RegularExpressions;

namespace boltprompt;

public static partial class UnicodeEscaper
{
    public static string Encode(string input)
    {
        var escaped = new StringBuilder();
        foreach (var c in input)
            escaped.Append(c >= 32 && c <= 127 ? c : $"\\u{(int)c:X4}");
        return escaped.ToString();
    }
    
    public static string Decode(string input) => UnicodeEscapeRegex().Replace(input, match =>
    {
        var decodedChar = (char)Convert.ToInt32(match.Groups[1].Value, 16);
        return decodedChar.ToString();
    });

    [GeneratedRegex(@"\\u([0-9A-Fa-f]{4})")]
    private static partial Regex UnicodeEscapeRegex();
}