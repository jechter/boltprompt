using NiceIO;

namespace Shelper;

public static class Prompt
{
    private static int _promptLength;
    private static int _scrollOffset;
    private static int _commandLength;
    private static int _commandLineCursorPosition;
    public static void SetCursorPosition(int commandLineCursorPosition)
    {
        var pos = Console.GetCursorPosition();
        _commandLineCursorPosition = commandLineCursorPosition;
        _scrollOffset = Math.Clamp(_scrollOffset, commandLineCursorPosition - Console.WindowWidth + _promptLength + 1, commandLineCursorPosition);
        Console.SetCursorPosition(_commandLineCursorPosition + _promptLength - _scrollOffset, pos.Top);
    }

    static string CurrentDirectoryNameForPrompt(NPath path) => path == NPath.HomeDirectory ? "~" : path.FileName;
    
    public static void RenderPrompt(string? commandline = null, string? selectedSuggestion = null)
    {
        var pos = Console.GetCursorPosition();
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.SetCursorPosition(0, pos.Top);
        var promptText = $"{CurrentDirectoryNameForPrompt(NPath.CurrentDirectory)}⚡️";
        Console.Write(promptText);
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write("\uE0B0 ");
        Console.ResetColor();
        _promptLength = promptText.Length + 2;
        if (commandline == null) return;
        var commandLineLastWord = Suggestor.SplitCommandIntoWords(commandline).LastOrDefault("");
        var selectedSuggestionSuffix = "";
        
        if (selectedSuggestion != null && commandLineLastWord.Length < selectedSuggestion.Length)
            selectedSuggestionSuffix = selectedSuggestion[commandLineLastWord.Length..];

        _commandLength = commandline.Length + selectedSuggestionSuffix.Length;
        
        var remainingSpace = Console.WindowWidth - _promptLength - _commandLength;
        var charactersToSkip = _scrollOffset;
        
        if (charactersToSkip > 0)
        {
            Console.Write("⋯");
            charactersToSkip++;
        }

        if (charactersToSkip < commandline.Length)
        {
            Console.Write(commandline[charactersToSkip..]);
            charactersToSkip = 0;
        }
        else
            charactersToSkip -= commandline.Length;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(selectedSuggestionSuffix[charactersToSkip..]);

        if (remainingSpace + _scrollOffset < 0)
        {
            Console.SetCursorPosition(Console.WindowWidth - 1, pos.Top);
            Console.Write("⋯");
        }


        Console.ResetColor();

        SuggestionConsoleViewer.ClearLineFromCursor();
        Console.SetCursorPosition(pos.Left, pos.Top);
    }
}