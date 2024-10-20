namespace boltprompt;

public static class SuggestionConsoleViewer
{
    public static void ShowSuggestions(int top, Suggestion[] suggestions, int selection, bool useColor = true)
    {
        BufferedConsole.Update();
        var pos = BufferedConsole.GetCursorPosition();
        var topLine = top;
        BufferedConsole.SetCursorPosition(0, topLine);
        var maxNumSuggestions = Configuration.Instance.NumSuggestions;
        if  (topLine + maxNumSuggestions > BufferedConsole.WindowHeight)
            for (var i=0; i<maxNumSuggestions; i++)
                BufferedConsole.WriteLine();
        while (topLine + maxNumSuggestions > BufferedConsole.WindowHeight)
        {
            topLine--;
            pos.Top--;
        }
        BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        var startLine = Math.Min(Math.Max(0, selection - maxNumSuggestions / 2), Math.Max(0, suggestions.Length - maxNumSuggestions));
        var line = topLine;
        var maxSuggestionLength = suggestions.Any() ? suggestions.Skip(startLine).Take(maxNumSuggestions).Select(s => Prompt.MeasureConsoleStringWidth(s.Text) + (s.Icon?.Length ?? 0)).Max() : 0;
        var descriptionStart = maxSuggestionLength + 5;
        var descriptionLength = BufferedConsole.WindowWidth - descriptionStart;
        for (var i=startLine; i < startLine + maxNumSuggestions; i++)
        {
            if (useColor)
                SetSuggestionColors(selection == i);

            BufferedConsole.SetCursorPosition(0, line);
            BufferedConsole.Bold = true;
            BufferedConsole.Write(selection == i ? "\u27a4  " : "   ");
            if (suggestions.Length > i)
            {
                if (suggestions[i].Icon != null)
                    BufferedConsole.Write($"{suggestions[i].Icon} ");
                var labelSize = BufferedConsole.WindowWidth - (suggestions[i].Icon != null ? 5 : 3) - 2;
                if (Prompt.MeasureConsoleStringWidth(suggestions[i].Text) > labelSize)
                {
                    BufferedConsole.Write("⋯");
                    BufferedConsole.Write($"{suggestions[i].Text[^labelSize..]} ");
                }
                else
                    BufferedConsole.Write(suggestions[i].Text);
            }

            BufferedConsole.Bold = false;

            if (useColor)
                SetSuggestionColors(selection == i);
            BufferedConsole.ClearEndOfLine();
            BufferedConsole.SetCursorPosition(descriptionStart, line);
            if (suggestions.Length > i)
            {
                var description = selection == i ? suggestions[i].Description : suggestions[i].SecondaryDescription ?? suggestions[i].Description;
                if (description != null)
                    BufferedConsole.Write(Prompt.SubstringWithMaxConsoleWidth(new (description.Replace('\n', ' ')), descriptionLength));
            }

            if (useColor)
                BufferedConsole.ResetColor();
            line++;
        }

        if (line < BufferedConsole.WindowHeight)
        {
            BufferedConsole.SetCursorPosition(0, line);
            BufferedConsole.ClearEndOfScreen();
        }

        BufferedConsole.SetCursorPosition(pos.Left, pos.Top);
        BufferedConsole.Flush();
    }

    private static void SetSuggestionColors(bool isSelected)
    {
        BufferedConsole.BackgroundColor = BufferedConsole.ColorForHtml(isSelected
            ? Configuration.Instance.SelectedSuggestionBackgroundColor
            : Configuration.Instance.SuggestionBackgroundColor);
        BufferedConsole.ForegroundColor = BufferedConsole.ColorForHtml(Configuration.Instance.SuggestionTextColor);
    }

    public static void Clear(int top)
    {
        ShowSuggestions(top, [], -1, false);
    }
}