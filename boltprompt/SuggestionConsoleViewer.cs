namespace boltprompt;

public static class SuggestionConsoleViewer
{
    private const int MaxSuggestions = 10;
    
    public static void ShowSuggestions(Suggestion[] suggestions, int selection, bool useColor = true)
    {
        BufferedConsole.Update();
        var pos = BufferedConsole.GetCursorPosition();
        var topLine = BufferedConsole.GetCursorPosition().Top + 1;
        if  (topLine + MaxSuggestions > BufferedConsole.WindowHeight)
            for (var i=0; i<MaxSuggestions; i++)
                BufferedConsole.WriteLine();
        while (topLine + MaxSuggestions > BufferedConsole.WindowHeight)
        {
            topLine--;
            pos.Top--;
        }
        BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        var startLine = Math.Min(Math.Max(0, selection - MaxSuggestions / 2), Math.Max(0, suggestions.Length - MaxSuggestions));
        var line = topLine;
        var maxSuggestionLength = suggestions.Any() ? suggestions.Skip(startLine).Take(MaxSuggestions).Select(s => s.Text.Length + (s.Icon?.Length ?? 0)).Max() : 0;
        var descriptionStart = maxSuggestionLength + 5;
        var descriptionLength = BufferedConsole.WindowWidth - descriptionStart;
        for (var i=startLine; i < startLine + MaxSuggestions; i++)
        {
            if (useColor)
            {
                BufferedConsole.BackgroundColor = selection == i
                    ? BufferedConsole.ConsoleColor.LightCyan
                    : BufferedConsole.ConsoleColor.Gray20;
                BufferedConsole.ForegroundColor = BufferedConsole.ConsoleColor.Gray5;
            }

            BufferedConsole.SetCursorPosition(0, line);
            BufferedConsole.Bold = true;
            BufferedConsole.Write(selection == i ? "\u27a4  " : "   ");
            if (suggestions.Length > i)
            {
                if (suggestions[i].Icon != null)
                    BufferedConsole.Write($"{suggestions[i].Icon} ");
                var labelSize = BufferedConsole.WindowWidth - (suggestions[i].Icon != null ? 5 : 3);
                if (suggestions[i].Text.Length > labelSize)
                {
                    BufferedConsole.Write("⋯");
                    BufferedConsole.Write($"{suggestions[i].Text[^labelSize..]} ");
                }
                else
                    BufferedConsole.Write(suggestions[i].Text);
            }

            BufferedConsole.Bold = false;

            BufferedConsole.ClearEndOfLine();
            BufferedConsole.SetCursorPosition(descriptionStart, line);
            if (suggestions.Length > i)
            {
                var description = selection == i ? suggestions[i].Description : suggestions[i].SecondaryDescription ?? suggestions[i].Description;
                if (description != null)
                    BufferedConsole.Write(new string(description.Replace('\n', ' ').Take(descriptionLength).ToArray()));
            }

            if (useColor)
                BufferedConsole.ResetColor();
            line++;
        }
        BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        BufferedConsole.Flush();
    }

    public static void Clear()
    {
        ShowSuggestions([], -1, false);
    }
}