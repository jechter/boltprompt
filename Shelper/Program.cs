using System.Diagnostics.CodeAnalysis;
using Shelper;

var suggestor = new Suggestor();
var prompt = "";
var selection = 0;
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
            if (prompt.Length > 0)
            {
                prompt = prompt[..^1];
                var pos= Console.GetCursorPosition();
                Console.SetCursorPosition(pos.Left-1, pos.Top);
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
        case ConsoleKey.RightArrow:
            CommitSelection();
            break;
        case ConsoleKey.Escape:
            selection = -1;
            break;
        case ConsoleKey.Enter:
            CommitSelection();
            ExitAndRunCommand(prompt);
            break;
        default:
            prompt += key.KeyChar;
            if (selection == -1)
                selection = 0;
            break;
    }

    suggestions = suggestor.SuggestionsForPrompt(prompt);
    if (selection >= suggestions.Length)
        selection = suggestions.Length > 0 ? 0 : -1;
    else if (selection < -1)
        selection = suggestions.Length - 1; 
    Prompt.RenderPrompt(prompt, selection > -1 ? suggestions[selection].Text : null);
    SuggestionConsoleViewer.ShowSuggestions(suggestions, selection);
    continue;

    void CommitSelection()
    {
        if (selection == -1) return;
        if (suggestions.Length >= selection)
        {
            var promptWords = prompt.Split(' ');
            promptWords[^1] = suggestions[selection].Text;
            prompt = string.Join(' ', promptWords);
            var pos= Console.GetCursorPosition();
            Console.SetCursorPosition(0, pos.Top);
            Prompt.RenderPrompt();
            Console.Write(prompt);
            SuggestionConsoleViewer.ClearLineFromCursor();
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