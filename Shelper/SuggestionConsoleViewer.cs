namespace Shelper;

public class SuggestionConsoleViewer
{
    private const int MaxSuggestions = 5;

    public static void ClearLineFromCursor()
    {
        // Get the current cursor position
        var currentLeft = Console.CursorLeft;
        var currentTop = Console.CursorTop;

        // Calculate the number of characters to clear
        var widthToClear = Console.WindowWidth - currentLeft;

        // Write spaces to clear the rest of the line
        Console.Write(new string(' ', widthToClear));

        // Reset the cursor to the original position
        Console.SetCursorPosition(currentLeft, currentTop);
    }
    
    public static void ShowSuggestions(Suggestion[] suggestions, int selection)
    {
        var pos = Console.GetCursorPosition();
        var topLine = Console.GetCursorPosition().Top + 1;
        if  (topLine + MaxSuggestions > Console.WindowHeight)
            for (var i=0; i<MaxSuggestions; i++)
                Console.WriteLine();
        while (topLine + MaxSuggestions > Console.WindowHeight)
        {
            topLine--;
            pos.Top--;
        }
        Console.SetCursorPosition(pos.Left, pos.Top);
        var descriptionStart = Console.WindowWidth / 3;
        var descriptionLength = Console.WindowWidth - descriptionStart;
        var startLine = Math.Min(Math.Max(0, selection - MaxSuggestions / 2), Math.Max(0, suggestions.Length - MaxSuggestions));
        var line = topLine;
        var maxSuggestionLength = suggestions.Any() ? suggestions.Select(s => s.Text.Length).Max() : 0;
        for (var i=startLine; i < startLine + MaxSuggestions; i++)
        {
            Console.SetCursorPosition(0, line);
            if (selection == i)
                Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(selection == i ? "➡ " : "  ");
            if (suggestions.Length > i)
            {
                if (suggestions[i].Icon != null)
                    Console.Write($"{suggestions[i].Icon} ");
                Console.Write(suggestions[i].Text);
            }

            ClearLineFromCursor();
            Console.SetCursorPosition(Math.Min(maxSuggestionLength + 3, Console.WindowWidth / 2), line);
            if (suggestions.Length > i)
            {
                var description = suggestions[i].Description;
                if (description != null)
                    Console.Write(new string(description.Take(descriptionLength).ToArray()));
            }

            Console.ResetColor();
            line++;
        }
        Console.SetCursorPosition(pos.Left, pos.Top);
    }

    public static void Clear()
    {
        ShowSuggestions([], -1);
    }
}