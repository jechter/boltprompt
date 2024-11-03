using System.Reflection;
using NiceIO;
using Wcwidth;

namespace boltprompt;

internal static class Prompt
{
    private static int _promptLength;
    private static int _scrollOffset;
    private static int _commandLength;
    private static int _commandLineCursorPosition;
    private static int _commandLineCursorRow = 0;

    public static int CursorPosition
    {
        get => _commandLineCursorPosition;
        set => SetCursorPosition(value);
    }

    private static void SetCursorPosition(int commandLineCursorPosition)
    {
        var pos = BufferedConsole.GetCursorPosition();
        _commandLineCursorPosition = commandLineCursorPosition;
        if (Configuration.Instance.ScrollLongCommandLine)
            _scrollOffset = Math.Clamp(_scrollOffset, commandLineCursorPosition - Console.WindowWidth + _promptLength + 1, commandLineCursorPosition);
        var cursorPosTotal = _commandLineCursorPosition + _promptLength - _scrollOffset;
        var targetRow = cursorPosTotal / Console.WindowWidth;
        BufferedConsole.SetCursorPosition(cursorPosTotal % Console.WindowWidth, pos.Top + (targetRow - _commandLineCursorRow));
        _commandLineCursorRow = targetRow;
    }

    private enum PathStyle
    {
        DirectoryNameOnly,
        Compact,
        Full
    }

    private static string NPathFileNameOrRoot(NPath path) => path.IsRoot ? "/" : path.FileName;
    private static string CompactPath(NPath path) => $"{string.Join("", path.RecursiveParents.Reverse().Where(p => p is { IsRoot: false, FileName.Length: > 0 }).Select(p => $"{p.FileName[0]}/"))}{NPathFileNameOrRoot(path)}";

    private static string CurrentDirectoryNameForPrompt(NPath path, PathStyle style) => path == NPath.HomeDirectory
        ? "~"
        : style switch
        {
            PathStyle.DirectoryNameOnly => NPathFileNameOrRoot(path),
            PathStyle.Compact => CompactPath(path.IsSameAsOrChildOf(NPath.HomeDirectory) ? $"~/{path.RelativeTo(NPath.HomeDirectory)}" : path),
            PathStyle.Full => path.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
        };

    public static string ComposePromptPrefixScheme((BufferedConsole.ConsoleColor bg, BufferedConsole.ConsoleColor fg, string label)[] parts)
    {
        var result = "";
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            result += $"\u001b[48;5;{(int)part.bg}m\u001b[38;5;{(int)part.fg}m{part.label}";
            if (i < parts.Length - 1)
                result += $"\u001b[48;5;{(int)parts[i + 1].bg}m";
            else
                result += "\u001b[0m";
            result += $"\u001b[38;5;{(int)part.bg}m\uE0B0";
        }
        result += "\u001b[0m ";
        return result;
    }
    
    public static string GetPromptPrefix(string scheme, string? commandLine = null)
    {
        var debug = Assembly.GetEntryAssembly()?.Location.ToNPath().Parent.Parent.FileName == "Debug";
        var promptChar = commandLine?.StartsWith('@') ?? false ? "🤖" : Environment.UserName == "root"? "\u2622\ufe0f " : debug ? "🪲" : "⚡️";
        return scheme
            .Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{host_name}", Environment.MachineName)
            .Replace("{user_name}", Environment.UserName)
            .Replace("{working_directory_name}", CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.DirectoryNameOnly))
            .Replace("{working_directory_short_path}", CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.Compact))
            .Replace("{working_directory_path}", CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.Full))
            .Replace("{prompt_symbol}", promptChar);
    }

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
    
    public static int RenderPrompt(string? commandline = null, string? selectedSuggestion = null)
    {
        BufferedConsole.Update();
        var pos = BufferedConsole.GetCursorPosition();
        BufferedConsole.SetCursorPosition(0, pos.Top - _commandLineCursorRow);
        BufferedConsole.ClearEndOfScreen();
        var promptText = GetPromptPrefix(Configuration.Instance.PromptPrefix, commandline);
        _promptLength = MeasureConsoleStringWidth(promptText);
        BufferedConsole.Write(promptText);
        
        if (commandline == null) 
            return pos.Top + 1;
        var selectedSuggestionSuffix = "";
        
        var parts = Suggestor.ParseCommandLine(commandline).ToArray();
        var partsIndexUpToCursor = PartsIndexUpToCursor(parts);

        var selectedWord = parts.Length > 0 ? parts[partsIndexUpToCursor - 1] : null;
        if (selectedSuggestion != null)
        {
            if (selectedWord == null)
                selectedSuggestionSuffix = selectedSuggestion;
            else
                selectedSuggestionSuffix = selectedWord.Type is Suggestor.CommandLinePart.PartType.Whitespace or Suggestor.CommandLinePart.PartType.Operator
                    ? selectedSuggestion
                    : selectedSuggestion[selectedWord.Text.Length..];
        }

        _commandLength = commandline.Length + selectedSuggestionSuffix.Length;
        
        var remainingSpace = Console.WindowWidth - _promptLength - _commandLength;
        var charactersToSkip = _scrollOffset;
        
        if (charactersToSkip > 0)
        {
            BufferedConsole.Write("⋯");
            charactersToSkip++;
        }
  
        charactersToSkip = PrintCommandLineParts(parts[..partsIndexUpToCursor], charactersToSkip);

        BufferedConsole.ForegroundColor = BufferedConsole.ColorForHtml(Configuration.Instance.AutocompleteTextColor); 
        if (charactersToSkip < selectedSuggestionSuffix.Length)
            BufferedConsole.Write(selectedSuggestionSuffix[charactersToSkip..]);
        BufferedConsole.ResetColor();

        charactersToSkip -= selectedSuggestionSuffix.Length;
        if (charactersToSkip < 0)
            charactersToSkip = 0;
        
        PrintCommandLineParts(parts[partsIndexUpToCursor..], charactersToSkip);

        if (Configuration.Instance.ScrollLongCommandLine && remainingSpace + _scrollOffset < 0)
        {
            BufferedConsole.SetCursorPosition(Console.WindowWidth - 1, pos.Top);
            BufferedConsole.Write("⋯");
        }
        else
            BufferedConsole.ClearEndOfLine();
        BufferedConsole.ResetColor();
        BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        SetCursorPosition(_commandLineCursorPosition);
        if (Configuration.Instance.ScrollLongCommandLine)
            return pos.Top + 1;
        var totalCommandLineAndPromptLength =
            _promptLength + MeasureConsoleStringWidth(commandline) + selectedSuggestionSuffix.Length;
        return pos.Top - _commandLineCursorRow + totalCommandLineAndPromptLength / Console.WindowWidth + 1;
    }

    public static int PartsIndexUpToCursor(Suggestor.CommandLinePart[] parts)
    {
        var partsIndexUpToCursor = 0;
        var commandLineLengthUpToCursorPart = 0;
        foreach (var part in parts)
        {
            commandLineLengthUpToCursorPart += part.Text.Length;
            partsIndexUpToCursor++;
            if (commandLineLengthUpToCursorPart >= _commandLineCursorPosition)
                break;
        }

        return partsIndexUpToCursor;
    }

    private static int PrintCommandLineParts(Suggestor.CommandLinePart[] parts, int charactersToSkip)
    {
        foreach (var part in parts)
        {
            BufferedConsole.Bold = part.Type switch
            {
                Suggestor.CommandLinePart.PartType.Command => true,
                Suggestor.CommandLinePart.PartType.Argument => false,
                Suggestor.CommandLinePart.PartType.Operator => true,
                Suggestor.CommandLinePart.PartType.Whitespace => false,
                Suggestor.CommandLinePart.PartType.Variable => false,
                _ => throw new ArgumentOutOfRangeException()
            };
            BufferedConsole.Underline = part.Argument?.Type switch
            {
                CommandInfo.ArgumentType.Directory => true,
                CommandInfo.ArgumentType.File => true,
                CommandInfo.ArgumentType.FileSystemEntry => true,
                CommandInfo.ArgumentType.Unknown => new NPath(part.Text).Exists(),
                _ => false
            };
            if (charactersToSkip < part.Text.Length)
                BufferedConsole.Write(part.Text[charactersToSkip..]);

            charactersToSkip -= part.Text.Length;
            if (charactersToSkip < 0)
                charactersToSkip = 0;
        }

        return charactersToSkip;
    }
}