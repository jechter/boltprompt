namespace Shelper;

public static class Prompt
{
    public static void SetCursorPosition(int commandLineCursorPosition)
    {
        var pos = Console.GetCursorPosition();
        Console.SetCursorPosition(commandLineCursorPosition + 3, pos.Top);
    }
    
    public static void RenderPrompt(string? commandline = null, string? selectedSuggestion = null)
    {
        var pos = Console.GetCursorPosition();
        Console.SetCursorPosition(0, pos.Top);
        Console.Write("⚡ ");
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