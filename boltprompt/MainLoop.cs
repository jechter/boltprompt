using System.Diagnostics.CodeAnalysis;

namespace boltprompt;

internal class MainLoop
{
    private string _commandLine = "";
    private string? _aiPrompt;
    private int _selection;
    private int _commandLineCursorPos;
    private int _screenWidth;
    private bool _needsRedraw;
    private bool _didShowSuggestions;
    private Suggestion[] _suggestions = [];
    
    public MainLoop()
    {
        _selection = -1;
        _screenWidth = Console.WindowWidth;
        Console.CancelKeyPress += ConsoleCancelKeyPress;
        KnownCommands.CommandInfoLoaded += _ => RequestRedraw();
        FileDescriptions.FileDescriptionLoaded += RequestRedraw;
        CustomArguments.CustomArgumentsLoaded += RequestRedraw;
        AISuggestor.AIDescriptionLoaded += RequestRedraw;
        Prompt.RenderPrompt();
    }

    private void RequestRedraw() => _needsRedraw = true;
    
    public void Run()
    {
        while (true)
        {
            if (Console.WindowWidth != _screenWidth)
            {
                if (_suggestions.Length != 0)
                {
                    if (Console.WindowWidth < _screenWidth)
                    {
                        BufferedConsole.ClearEndOfScreem();
                        BufferedConsole.Flush();
                    }
                }
                RequestRedraw();
                _screenWidth = Console.WindowWidth;
            }

            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey();
                BufferedConsole.Update();
                RequestRedraw();
                if (key.Modifiers == ConsoleModifiers.Control)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.A:
                            _commandLineCursorPos = 0;
                            Prompt.SetCursorPosition(_commandLineCursorPos);
                            break;
                        case ConsoleKey.E:
                            _commandLineCursorPos = _commandLine.Length;
                            Prompt.SetCursorPosition(_commandLineCursorPos);
                            break;
                    }
                }
                else switch (key.Key)
                {
                    case ConsoleKey.Delete:
                    {
                        if (_commandLine.Length > 0 && _commandLineCursorPos < _commandLine.Length)
                            _commandLine = _commandLine[.._commandLineCursorPos] + _commandLine[(_commandLineCursorPos + 1)..];
                        break;
                    }
                    case ConsoleKey.Backspace:
                    {
                        if (_commandLine.Length > 0 && _commandLineCursorPos > 0)
                        {
                            _commandLine = _commandLine[..(_commandLineCursorPos - 1)] + _commandLine[_commandLineCursorPos..];
                            Prompt.SetCursorPosition(--_commandLineCursorPos);
                        }
                        break;
                    }
                    case ConsoleKey.DownArrow:
                        _selection++;
                        break;
                    case ConsoleKey.UpArrow:
                        _selection--;
                        if (_selection < 0)
                            _selection = -2;
                        break;
                    case ConsoleKey.LeftArrow:
                        if (_commandLineCursorPos > 0)
                            Prompt.SetCursorPosition(--_commandLineCursorPos);
                        break;
                    case ConsoleKey.RightArrow:
                        if (_commandLineCursorPos < _commandLine.Length)
                            Prompt.SetCursorPosition(++_commandLineCursorPos);
                        else
                            CommitSelection();
                        break;
                    case ConsoleKey.Tab:
                        CommitSelection();
                        break;
                    case ConsoleKey.Escape:
                        if (Console.KeyAvailable && key.KeyChar == 0x1b) 
                        {
                            // handle bracketed paste escape sequence
                            while (Console.KeyAvailable && key.KeyChar != '~')
                                key = Console.ReadKey();
                            break;
                        }

                        if (_commandLine.StartsWith('@'))
                        {
                            _commandLine = "";
                            _commandLineCursorPos = 0;
                            Prompt.SetCursorPosition(_commandLineCursorPos);
                        }

                        _selection = -1;
                        break;
                    case ConsoleKey.Enter:
                        CommitSelection();
                        if (!_commandLine.StartsWith("@"))
                            ExitAndRunCommand(_commandLine);
                        break;
                    default:
                        _commandLine = _commandLine[.._commandLineCursorPos] + key.KeyChar +
                                       _commandLine[_commandLineCursorPos..];
                        Prompt.SetCursorPosition(++_commandLineCursorPos);
                        if (_selection == -1)
                            _selection = 0;
                        break;
                }
            }

            RenderPromptAndSuggestionsIfNeeded();
            Thread.Sleep(100);
        }
    }

    private void CommitSelection()
    {
        if (_selection == -1) return;
        if (_suggestions.Length != 0 && _suggestions.Length >= _selection)
        {
            var promptWords = Suggestor.SplitCommandIntoWords(_commandLine);
            if (promptWords.Length == 0)
                _commandLine = _suggestions[_selection].Text; 
            else if (_commandLine.StartsWith('@'))
            {
                _aiPrompt = _commandLine[1..];
                _commandLine = _suggestions[_selection].Text;
            }
            else
            {
                promptWords[^1] = _suggestions[_selection].Text;
                _commandLine = string.Join(' ', promptWords);
            }
            _commandLineCursorPos = _commandLine.Length;
            Prompt.SetCursorPosition(_commandLineCursorPos);
        }
        // In most cases we don't want to show new suggestions after committing before typing.
        // But if we selected an AI prompt from history, we want to get AI suggestions right away.
        // And if we selected a path, we want to be able to continue said path
        _selection = _commandLine.StartsWith('@') || _commandLine.EndsWith('/') ? 0 : -1;
    }

    private void RenderPromptAndSuggestionsIfNeeded()
    {
        if (!_needsRedraw) return;
        _suggestions = Suggestor.SuggestionsForPrompt(_commandLine);
        if (_selection >= _suggestions.Length)
            _selection = _suggestions.Length > 0 ? 0 : -1;
        else if (_selection < -1)
            _selection = _suggestions.Length - 1;
        Prompt.RenderPrompt(_commandLine, _selection > -1 && !_commandLine.StartsWith("@") ? _suggestions[_selection].Text : null);
        if (_suggestions.Length > 0 && _selection != -1)
        {
            _didShowSuggestions = true;
            SuggestionConsoleViewer.ShowSuggestions(_suggestions, _selection);
        }
        else if (_didShowSuggestions)
            SuggestionConsoleViewer.Clear();
        _needsRedraw = false;
    }

    private void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
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
        History.AddCommandToHistory(command, _aiPrompt);
        Environment.Exit(0);
    }
}