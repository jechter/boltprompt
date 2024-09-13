using NiceIO;

namespace Shelper;

public static class Prompt
{
    private static int _promptLength;
    public static void SetCursorPosition(int commandLineCursorPosition)
    {
        var pos = Console.GetCursorPosition();
        Console.SetCursorPosition(commandLineCursorPosition + _promptLength, pos.Top);
    }
    
    public static void RenderPrompt(string? commandline = null, string? selectedSuggestion = null)
    {
        var pos = Console.GetCursorPosition();
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.SetCursorPosition(0, pos.Top);
        Console.Write(NPath.CurrentDirectory.FileName);
        Console.Write("⚡️");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write("\uE0B0 ");
        Console.ResetColor();
        _promptLength = Console.GetCursorPosition().Left;
        if (commandline == null) return;
        Console.Write(commandline);
        var commandLineLastWord = commandline.Split(' ').Last();
        if (selectedSuggestion != null && commandLineLastWord.Length < selectedSuggestion.Length)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(selectedSuggestion[commandLineLastWord.Length..]);
            Console.ResetColor();
        }
        SuggestionConsoleViewer.ClearLineFromCursor();
        Console.SetCursorPosition(pos.Left, pos.Top);
    }
}