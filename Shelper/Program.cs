using System.Diagnostics.CodeAnalysis;
using Shelper;

var suggestor = new Suggestor();
var commandLine = "";
var selection = 0;
var commandLineCursorPos = 0;
Console.CancelKeyPress += ConsoleCancelKeyPress;
Suggestion[] suggestions = [];
Prompt.RenderPrompt();

while (true)
{
    var key = Console.ReadKey();
    switch (key.Key)
    {
        case ConsoleKey.Backspace:
        case ConsoleKey.Delete:
        {
            if (commandLine.Length > 0 && commandLineCursorPos > 0)
            {
                commandLine = commandLine[..(commandLineCursorPos - 1)] + commandLine[commandLineCursorPos..];
                Prompt.SetCursorPosition(--commandLineCursorPos);
            }
            break;
        }
        case ConsoleKey.DownArrow:
            selection++;
            break;
        case ConsoleKey.UpArrow:
            selection--;
            if (selection < 0)
                selection = -2;
            break;
        case ConsoleKey.LeftArrow:
            if (commandLineCursorPos > 0)
                Prompt.SetCursorPosition(--commandLineCursorPos);
            break;
        case ConsoleKey.RightArrow:
            if (commandLineCursorPos < commandLine.Length)
                Prompt.SetCursorPosition(++commandLineCursorPos);
            else
                CommitSelection();
            break;
        case ConsoleKey.Tab:
            CommitSelection();
            break;
        case ConsoleKey.Escape:
            selection = -1;
            break;
        case ConsoleKey.Enter:
            CommitSelection();
            ExitAndRunCommand(commandLine);
            break;
        default:
            commandLine = commandLine[..commandLineCursorPos] + key.KeyChar + commandLine[commandLineCursorPos..];
            commandLineCursorPos++;
            if (selection == -1)
                selection = 0;
            break;
    }

    suggestions = suggestor.SuggestionsForPrompt(commandLine);
    if (selection >= suggestions.Length)
        selection = suggestions.Length > 0 ? 0 : -1;
    else if (selection < -1)
        selection = suggestions.Length - 1; 
    Prompt.RenderPrompt(commandLine, selection > -1 ? suggestions[selection].Text : null);
    SuggestionConsoleViewer.ShowSuggestions(suggestions, selection);
    continue;

    void CommitSelection()
    {
        if (selection == -1) return;
        if (suggestions.Length != 0 && suggestions.Length >= selection)
        {
            var promptWords = commandLine.Split(' ');
            promptWords[^1] = suggestions[selection].Text;
            commandLine = string.Join(' ', promptWords);
            Prompt.RenderPrompt(commandLine);
            commandLineCursorPos = commandLine.Length;
            Prompt.SetCursorPosition(commandLineCursorPos);
        }
        selection = -1;
    }
}

void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    ExitAndRunCommand();
}

[DoesNotReturn]
void ExitAndRunCommand(string command = "")
{
    SuggestionConsoleViewer.Clear();
    Console.WriteLine();
    File.WriteAllText("/tmp/custom-command", command);
    History.AddCommandToHistory(command);
    Environment.Exit(0);
}