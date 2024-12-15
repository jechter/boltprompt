using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Wcwidth;

namespace boltprompt;

// Writing to the Console is slow in some Terminals (e.g. iTerm2). Buffer writes, and do them in bulk.
internal static partial class BufferedConsole
{
    public record RgbColor(byte R, byte G, byte B);
    
    public static RgbColor ParseHtmlColor(string html)
    {
        var match = HtmlColorRegex().Match(html);
        if (!match.Success) return new(0,0,0);
        return new(
            byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber),
            byte.Parse(match.Groups[2].Value, NumberStyles.HexNumber),
            byte.Parse(match.Groups[3].Value, NumberStyles.HexNumber)
            );
    }
    
    public static ConsoleColor ColorForHtml(RgbColor rgb)
    {
        return (ConsoleColor)(16 + 36*(rgb.R*5/255) + 6*(rgb.G*5/255) + rgb.B*5/255);
    }
    
    public static ConsoleColor ColorForHtml(string html)
    {
        var rgb = ParseHtmlColor(html);
        return ColorForHtml(rgb);
    }
    
    public enum ConsoleColor
    {
        Black,
        Red,
        Green,
        Yellow,
        Blue,
        Magenta,
        Cyan,
        White,
        LightBlack,
        LightRed,
        LightGreen,
        LightYellow,
        LightBlue,
        LightMagenta,
        LightCyan,
        LightWhite,      
        Gray0 = 232,
        Gray1 = 233,
        Gray2 = 234,
        Gray3 = 235,
        Gray4 = 236,
        Gray5 = 237,
        Gray6 = 238,
        Gray7 = 239,
        Gray8 = 240,
        Gray9 = 241,
        Gray10 = 242,
        Gray11 = 243,
        Gray12 = 244,
        Gray13 = 245,
        Gray14 = 246,
        Gray15 = 247,
        Gray16 = 248,
        Gray17 = 249,
        Gray18 = 250,
        Gray19 = 251,
        Gray20 = 252,
        Gray21 = 253,
        Gray22 = 254,
        Gray23 = 255
    }
    
    private static StringBuilder _buffer = new();
    private static int _left = 0, _top = 0, _windowWidth = 0, _windowHeight = 0;
    public static bool Debug = false;

    public static void Update()
    {
        Flush();
        _left = Console.CursorLeft;
        _top = Console.CursorTop;
        _windowWidth = Console.WindowWidth;
        _windowHeight = Console.WindowHeight;
    }
    
    public static (int Left, int Top) GetCursorPosition() => (_left, _top);
    public static int CursorLeft => _left;
    public static int CursorTop => _top;
    public static int WindowWidth => _windowWidth;
    public static int WindowHeight => _windowHeight;

    private static string ConsoleEscape(string ctrl) => $"\u001b[{ctrl}";
    
    public static void SetCursorPosition(int left, int top)
    {
        _buffer.Append(SetCursorPositionEsc(left, top));
        _left = left;
        _top = top;
        if (Debug)
            Flush();
    }
    
    public static string SetCursorPositionEsc(int left, int top) => ConsoleEscape($"{top + 1};{left + 1}H");
    
    public static void Write(string chars)
    {
        _buffer.Append(chars);
        _left += MeasureConsoleStringWidth(chars);
        if (_left >= _windowWidth) 
            _left -= _windowWidth;
        if (Debug)
            Flush();
    }
    
    public static void WriteLine()
    {
        _buffer.Append('\n');
        _left = 0;
        if (_top < _windowHeight - 1)
            _top++;
        if (Debug)
            Flush();
    }
    
    public static ConsoleColor BackgroundColor {
        set => _buffer.Append(BackgroundColorEsc(value));
    }

    public static string BackgroundColorEsc(ConsoleColor color) => ConsoleEscape($"48;5;{(int)color}m");
    public static string BackgroundColorEsc(string color) => BackgroundColorEsc(ColorForHtml(color));
    public static string BackgroundColorEsc(RgbColor color) => BackgroundColorEsc(ColorForHtml(color));

    public static ConsoleColor ForegroundColor {
        set => _buffer.Append(ForegroundColorEsc(value));
    }

    public static string ForegroundColorEsc(ConsoleColor color) => ConsoleEscape($"38;5;{(int)color}m");
    public static string ForegroundColorEsc(string color) => ForegroundColorEsc(ColorForHtml(color));
    public static string ForegroundColorEsc(RgbColor color) => ForegroundColorEsc(ColorForHtml(color));

    public static bool Bold
    {
        set => _buffer.Append(BoldEsc(value));
    }
    
    public static string BoldEsc(bool enable) => ConsoleEscape(enable ? "1m" : "22m");
    
    public static bool CrossedOut
    {
        set => _buffer.Append(CrossedOutEsc(value));
    }
    
    public static string CrossedOutEsc(bool enable) => ConsoleEscape(enable ? "9m" : "29m");

    public static bool Underline
    {
        set => _buffer.Append(UnderlineEsc(value));
    }
    
    public static string UnderlineEsc(bool enable) => ConsoleEscape(enable ? "4m" : "24m");

    public static void ResetColor() => _buffer.Append(ResetColorEsc());
    
    public static string ResetColorEsc() => ConsoleEscape("0m");

    public static void ClearEndOfLine() => _buffer.Append(ClearEndOfLineEsc());
    
    public static string ClearEndOfLineEsc() => ConsoleEscape("0K");

    public static string MoveToStartOfLineEsc() => ConsoleEscape("0G");

    public static void ClearEndOfScreen() => _buffer.Append(ClearEndOfScreenEsc());
    
    public static string ClearEndOfScreenEsc() => ConsoleEscape("0J");

    public static void Flush()
    {
        Console.Write(_buffer.ToString());
        _buffer.Clear();
        if (!Debug) return;
        var realpos = Console.GetCursorPosition();
        if (realpos.Left != _left)
            throw new Exception(
                $"Invalid cursor left position: {realpos.Left}, expected {_left}. Window size: {_windowWidth}x{_windowHeight}");
        if (realpos.Top != _top)
            throw new Exception(
                $"Invalid cursor top position: {realpos.Top}, expected {_top}. Window size: {_windowWidth}x{_windowHeight}");
    }

    [GeneratedRegex("([A-Fa-f0-9]{2})([A-Fa-f0-9]{2})([A-Fa-f0-9]{2})")]
    private static partial Regex HtmlColorRegex();
    
    public static int MeasureConsoleStringWidth(string text, Action<char, int>? callback = null)
    {
        var width = 0;
        var index = 0;
        var isControlSequence = false;
        while (index < text.Length)
        {
            var ch = text[index++];
            callback?.Invoke(ch, width);
            if (isControlSequence)
            {
                if (ch == 'm')
                    isControlSequence = false;
            }
            else
            {
                if (ch == '\u001b')
                    isControlSequence = true;
                else
                    width += UnicodeCalculator.GetWidth(ch);
            }
        }
        return width;
    }
    
    public static string SubstringWithMaxConsoleWidth(string text, int maxWidth)
    {
        var result = "";
        MeasureConsoleStringWidth(text, (ch, width) =>
        {
            if (width <= maxWidth)
                result += ch;
        });
        return result;
    }
}