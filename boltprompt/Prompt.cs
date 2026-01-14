using System.Reflection;
using NiceIO;

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
        
        return UnicodeEscaper.Encode(result);
    }

    
    public static bool IsAIPrompt(string commandLine) => commandLine.StartsWith(Configuration.Instance.AIPromptPrefix) || commandLine.StartsWith(Configuration.Instance.AIQuestionPrefix);

    public static string GetPromptPrefix(string scheme, string? commandLine = null)
    {
        var debug = Assembly.GetEntryAssembly()?.Location.ToNPath().Parent.Parent.FileName == "Debug";
        var promptChar = IsAIPrompt(commandLine ?? "") 
            ? "🤖" 
            : Environment.UserName == "root" 
                ? "\u2622\ufe0f " 
                : debug 
                    ? "🪲" 
                    : "⚡️";
        scheme = UnicodeEscaper.Decode(scheme);
        if (!TerminalUtility.CurrentTerminalHasNerdFont())
            scheme = scheme.Replace("\uE0B0", "");
        return scheme
            .Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{host_name}", Environment.MachineName)
            .Replace("{user_name}", Environment.UserName)
            .Replace("{working_directory_name}",
                CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.DirectoryNameOnly))
            .Replace("{working_directory_short_path}",
                CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.Compact))
            .Replace("{working_directory_path}", CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.Full))
            .Replace("{prompt_symbol}", promptChar);
    }
    
    public static int RenderPrompt(string? commandline = null, string? selectedSuggestion = null)
    {
        BufferedConsole.Update();
        var pos = BufferedConsole.GetCursorPosition();

        // Set terminal window title to current working directory
        BufferedConsole.SetTerminalTitle(CurrentDirectoryNameForPrompt(NPath.CurrentDirectory, PathStyle.Full));

        BufferedConsole.SetCursorPosition(0, pos.Top - _commandLineCursorRow);
        BufferedConsole.ClearEndOfScreen();
        var promptText = GetPromptPrefix(Configuration.Instance.Prompt, commandline);
        _promptLength = BufferedConsole.MeasureConsoleStringWidth(promptText);
        BufferedConsole.Write(promptText);
        
        if (commandline == null) 
            return pos.Top + 1;
        var selectedSuggestionSuffix = "";
        
        var parts = CommandLineParser.ParseCommandLine(commandline).ToArray();
        var partsIndexUpToCursor = PartsIndexUpToCursor(parts);

        var selectedWord = parts.Length > 0 ? parts[partsIndexUpToCursor - 1] : null;
        var selectedWordWillBeDeletedBySuggestion = false;
        var selectedWordIsPath = selectedWord is { Type: CommandLineParser.CommandLinePart.PartType.Argument, Argument.MayBeFileSystemEntry: true }
                                 && selectedWord.Text.Contains('/');
        var selectedWordPathSlashIndex = selectedWordIsPath ? selectedWord!.Text.LastIndexOf('/') : 0;

        if (selectedSuggestion != null)
        {
            if (selectedWord == null)
                selectedSuggestionSuffix = selectedSuggestion;
            else
            {
                if (selectedWord.Type is CommandLineParser.CommandLinePart.PartType.Whitespace
                    or CommandLineParser.CommandLinePart.PartType.Operator)
                    selectedSuggestionSuffix = selectedSuggestion;
                else
                {
                    if (selectedSuggestion.StartsWith(selectedWord.Text))
                        selectedSuggestionSuffix = selectedSuggestion[selectedWord.Text.Length..];
                    else
                    {
                        selectedWordWillBeDeletedBySuggestion = true;
                        selectedSuggestionSuffix = selectedSuggestion[selectedWordPathSlashIndex..];
                    }
                }
            }
        }

        _commandLength = commandline.Length + selectedSuggestionSuffix.Length;
        
        var remainingSpace = Console.WindowWidth - _promptLength - _commandLength;
        var charactersToSkip = _scrollOffset;
        
        if (charactersToSkip > 0)
        {
            BufferedConsole.Write("⋯");
            charactersToSkip++;
        }
  
        if (partsIndexUpToCursor > 0)
            charactersToSkip = PrintCommandLineParts(parts[..(partsIndexUpToCursor - 1)], charactersToSkip);
        if (selectedWord != null)
        {
            if (selectedWordIsPath)
                charactersToSkip = PrintCommandLineParts([selectedWord with {Text = selectedWord.Text[..selectedWordPathSlashIndex]} ], charactersToSkip);        
            if (selectedWordWillBeDeletedBySuggestion)
            {
                BufferedConsole.ForegroundColor = BufferedConsole.ColorForHtml("ff6060");
                BufferedConsole.CrossedOut = true;
            }
            charactersToSkip = PrintCommandLineParts([ 
                selectedWordIsPath 
                    ? selectedWord with{ Text = selectedWord.Text[selectedWordPathSlashIndex..]} 
                    : selectedWord], 
                charactersToSkip);
            BufferedConsole.ResetColor();
            BufferedConsole.CrossedOut = false;
        }


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
            BufferedConsole.ResetColor();
            BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        }
        else
        {
            BufferedConsole.ClearEndOfLine();
            BufferedConsole.ResetColor();
            if (pos.Top == Console.WindowHeight - 1)
            {
                // If we are on the last row in the Terminal window, and the prompt is longer than one row, printing it
                // will scroll us down - we have to compensate for that.
                while (remainingSpace < 0)
                {
                    remainingSpace += Console.WindowWidth;
                    pos.Top--;
                }
                BufferedConsole.SetCursorPosition(pos.Left, pos.Top + _commandLineCursorRow);
            }
            else
                BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        }
        
        SetCursorPosition(_commandLineCursorPosition);
        if (Configuration.Instance.ScrollLongCommandLine)
            return pos.Top + 1;
        var totalCommandLineAndPromptLength =
            _promptLength + BufferedConsole.MeasureConsoleStringWidth(commandline) + selectedSuggestionSuffix.Length;
        return pos.Top - _commandLineCursorRow + totalCommandLineAndPromptLength / Console.WindowWidth + 1;
    }

    public static int PartsIndexUpToCursor(CommandLineParser.CommandLinePart[] parts)
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

    private static int PrintCommandLineParts(CommandLineParser.CommandLinePart[] parts, int charactersToSkip)
    {
        foreach (var part in parts)
        {
            BufferedConsole.Bold = part.Type switch
            {
                CommandLineParser.CommandLinePart.PartType.Command => true,
                CommandLineParser.CommandLinePart.PartType.Argument => false,
                CommandLineParser.CommandLinePart.PartType.Operator => true,
                CommandLineParser.CommandLinePart.PartType.Whitespace => false,
                CommandLineParser.CommandLinePart.PartType.Variable => false,
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