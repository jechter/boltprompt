using NiceIO;

namespace boltprompt;

public static class SuggestionConsoleViewer
{
    private static bool PrintStringMarkingPrefix(string text, string prefix, int maxSize, bool cullAtBeginning)
    {
        var startIndex = 0;
        var endIndex = text.Length;
        if (Prompt.MeasureConsoleStringWidth(text) > maxSize)
        {
            if (cullAtBeginning)
            {
                BufferedConsole.Write("⋯");
                startIndex = text.Length - maxSize - 1;
            }
            else
                endIndex = Prompt.SubstringWithMaxConsoleWidth(text, maxSize).Length;
        }
        
        var prefixStartIndex = text.IndexOf(prefix, StringComparison.InvariantCultureIgnoreCase);
        var prefixEndIndex = prefixStartIndex == -1 ? prefixStartIndex : prefixStartIndex + prefix.Length;

        if (prefixStartIndex < startIndex)
            prefixStartIndex = startIndex;
        if (prefixEndIndex < startIndex)
            prefixEndIndex = startIndex;
        if (prefixStartIndex >= endIndex)
            prefixStartIndex = endIndex;
        if (prefixEndIndex >= endIndex)
            prefixEndIndex = endIndex;
        
        BufferedConsole.Write(text[startIndex..prefixStartIndex]);
        BufferedConsole.Underline = true;
        BufferedConsole.Write(text[prefixStartIndex..prefixEndIndex]);
        BufferedConsole.Underline = false;
        BufferedConsole.Write(text[prefixEndIndex..endIndex]);
        return prefixStartIndex != prefixEndIndex;
    }
    
    public static void ShowSuggestions(int top, Suggestion[] suggestions, string commandLine, int selection, bool useColor = true)
    {
        var pos = BufferedConsole.GetCursorPosition();
        var topLine = top;
        BufferedConsole.SetCursorPosition(0, topLine);
        var maxNumSuggestions = Configuration.Instance.NumSuggestions;

        // topline is the start of suggestions
        // if topline is beyond the last line, add maxNumSuggestion new lines to have space for suggestions 
        if (topLine >= BufferedConsole.WindowHeight)
            for (var i=0; i<maxNumSuggestions; i++)
                BufferedConsole.WriteLine();
        // otherwise, if we don't have space all the way, add maxNumSuggestion-1 new lines, as we already are on the first line
        else if  (topLine + maxNumSuggestions > BufferedConsole.WindowHeight)
            for (var i=0; i<maxNumSuggestions - 1; i++)
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
        var descriptionLength = BufferedConsole.WindowWidth - descriptionStart - 1;

        var parts = CommandLineParser.ParseCommandLine(commandLine).ToArray();
        var partsIndexUpToCursor = Prompt.PartsIndexUpToCursor(parts);
        var suggestionPrefix = "";

        if (partsIndexUpToCursor > 0)
        {
            var suggestionPart = parts[partsIndexUpToCursor - 1];
            if (suggestionPart.Type != CommandLineParser.CommandLinePart.PartType.Whitespace)
            {
                suggestionPrefix = suggestionPart.Text;
                if (suggestionPart is { Type: CommandLineParser.CommandLinePart.PartType.Argument, Argument.MayBeFileSystemEntry: true })
                    suggestionPrefix = suggestionPart.Text.ToNPath().FileName;
            }
        }

        for (var i=startLine; i < startLine + maxNumSuggestions; i++)
        {
            if (useColor)
                SetSuggestionColors(selection == i);
            
            BufferedConsole.SetCursorPosition(0, line);
            BufferedConsole.Bold = true;
            BufferedConsole.Write(selection == i ? "\u27a4  " : "   ");
            var didMarkPrefix = false;
            if (suggestions.Length > i)
            {
                if (suggestions[i].Icon != null)
                    BufferedConsole.Write($"{suggestions[i].Icon} ");
                var labelSize = BufferedConsole.WindowWidth - (suggestions[i].Icon != null ? 5 : 3) - 2;
                didMarkPrefix = PrintStringMarkingPrefix(suggestions[i].Text, suggestionPrefix, labelSize, true);
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
                {
                    
                    //description = Prompt.SubstringWithMaxConsoleWidth(new(description.Replace('\n', ' ')), descriptionLength);
                    PrintStringMarkingPrefix(description, didMarkPrefix ? "" : suggestionPrefix, descriptionLength, false);
                }
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
        ShowSuggestions(top, [], "", -1, false);
    }
}