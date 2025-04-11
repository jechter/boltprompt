using NiceIO;

namespace boltprompt;

internal class MainLoop
{
    private const int MinSelectionValue = -2;
    private const int NoneSelected = -1;
    private const int SelectionNotShown = -2;
    private string _commandLine = "";
    private string? _aiPrompt;
    private int _selection;
    private int _screenWidth;
    private bool _needsRedraw;
    private bool _didShowSuggestions;
    private bool _suggestionsHidden = true;
    private readonly NPath _outputCommand;
    private Suggestion[] _suggestions = [];
    
    public MainLoop(string? outputCommand)
    {
        _outputCommand = outputCommand ?? "/tmp/custom-command";
        _selection = SelectionNotShown;
        _screenWidth = Console.WindowWidth;
        // Most commands will end output with a new line. But if they don't, we don't want to
        // overwrite the last line of output with our prompt (which is always at the beginning
        // of the line). Instead, add a newline.
        if (Console.CursorLeft > 0)
            Console.WriteLine();
        Console.CancelKeyPress += ConsoleCancelKeyPress;
        KnownCommands.CommandInfoLoaded += _ => RequestRedraw();
        FileDescriptions.FileDescriptionLoaded += RequestRedraw;
        CustomArguments.CustomArgumentsLoaded += () =>
        {
            // if suggestions aren't showing, show them again, as there may be new ones.
            if (_selection == SelectionNotShown)
                _selection = NoneSelected;
            RequestRedraw();
        };
        AISuggestor.AIDescriptionLoaded += RequestRedraw;
        Prompt.RenderPrompt();
        BufferedConsole.Flush();
    }

    private void RequestRedraw() => _needsRedraw = true;

    private bool IsAIPrompt => Prompt.IsAIPrompt(_commandLine);
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
                        BufferedConsole.ClearEndOfScreen();
                        BufferedConsole.Flush();
                    }
                }
                RequestRedraw();
                _screenWidth = Console.WindowWidth;
            }

            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                BufferedConsole.Update();
                RequestRedraw();
                if (key.Modifiers == ConsoleModifiers.Control)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.A:
                            Prompt.CursorPosition = 0;
                            break;
                        case ConsoleKey.E:
                            Prompt.CursorPosition = _commandLine.Length;
                            break;
                    }
                }
                else switch (key.Key)
                {
                    case ConsoleKey.Delete:
                    {
                        if (_commandLine.Length > 0 && Prompt.CursorPosition < _commandLine.Length)
                            _commandLine = _commandLine[..Prompt.CursorPosition] + _commandLine[(Prompt.CursorPosition + 1)..];
                        break;
                    }
                    case ConsoleKey.Backspace:
                    {
                        if (_commandLine.Length > 0 && Prompt.CursorPosition > 0)
                        {
                            _commandLine = _commandLine[..(Prompt.CursorPosition - 1)] + _commandLine[Prompt.CursorPosition..];
                            if (_selection > NoneSelected)
                                _selection = NoneSelected;
                            Prompt.CursorPosition--;
                        }
                        break;
                    }
                    case ConsoleKey.DownArrow:
                        _suggestionsHidden = false;
                        _selection++;
                        break;
                    case ConsoleKey.UpArrow:
                        _suggestionsHidden = false;
                        _selection--;
                        if (_selection < 0)
                            _selection = -3;
                        break;
                    case ConsoleKey.LeftArrow:
                        if (Prompt.CursorPosition > 0)
                        {
                            Prompt.CursorPosition--;
                            if (_selection >= 0)
                                _selection = SelectionNotShown;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                        if (Prompt.CursorPosition < _commandLine.Length)
                            Prompt.CursorPosition++;
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

                        if (_commandLine.StartsWith(Configuration.Instance.AIPromptPrefix))
                        {
                            _commandLine = "";
                            Prompt.CursorPosition = 0;
                        }

                        _selection = SelectionNotShown;
                        break;
                    case ConsoleKey.Enter:
                        CommitSelection();
                        if (!IsAIPrompt)
                        {
                            SetupRunCommand(_commandLine);
                            return;
                        }

                        if (_commandLine.StartsWith(Configuration.Instance.AIQuestionPrefix))
                        {
                            RespondToAIQuestion();
                            return;
                        }

                        break;
                    default:
                        _commandLine = _commandLine[..Prompt.CursorPosition] + key.KeyChar +
                                       _commandLine[Prompt.CursorPosition..];
                        Prompt.CursorPosition++;
                        if (_selection is SelectionNotShown or NoneSelected && _commandLine != Configuration.Instance.AIQuestionPrefix)
                            _selection = 0;
                        break;
                }
            }

            RenderPromptAndSuggestionsIfNeeded();
            BufferedConsole.Flush();
            Thread.Sleep(100);
        }
    }

    private void RespondToAIQuestion()
    {
        var top = Prompt.RenderPrompt(_commandLine);
        SuggestionConsoleViewer.Clear(top);
        BufferedConsole.Flush();
        Console.WriteLine();
        _aiPrompt = _commandLine[Configuration.Instance.AIQuestionPrefix.Length..];
        History.AddCommandToHistory(_commandLine, _aiPrompt);
        var cancellation = new CancellationTokenSource();
        var task = AISuggestor.RespondToAIQuestion(_aiPrompt, cancellation.Token);
        Console.CancelKeyPress -= ConsoleCancelKeyPress;
        Console.CancelKeyPress += (_,e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        while (!task.IsCompleted)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                    cancellation.Cancel();
            }
            Thread.Sleep(10);
        }
        Console.WriteLine();
        _outputCommand.CreateFile();
    }

    private void CommitSelection()
    {
        UpdateSuggestionsAndSelection();
        if (_selection is SelectionNotShown or NoneSelected) return;
        if (_suggestions.Length != 0 && _suggestions.Length >= _selection)
        {
            var selection = _suggestions[_selection];
            if (_commandLine.StartsWith(Configuration.Instance.AIPromptPrefix))
            {
                _aiPrompt = _commandLine[Configuration.Instance.AIPromptPrefix.Length..];
                _commandLine = selection.Text;
                Prompt.CursorPosition = _commandLine.Length;
            }
            else
            {
                var parts = CommandLineParser.ParseCommandLine(_commandLine).ToArray();
                var partsIndexUpToCursor = Prompt.PartsIndexUpToCursor(parts);
                var newPart = new CommandLineParser.CommandLinePart(selection.Text);
                if (partsIndexUpToCursor == parts.Length &&
                    selection.Argument?.Type is not CommandInfo.ArgumentType.Flag && 
                    selection.Argument?.Type is not CommandInfo.ArgumentType.File && 
                    selection.Argument?.Type is not CommandInfo.ArgumentType.Directory &&
                    selection.Argument?.Type is not CommandInfo.ArgumentType.FileSystemEntry)
                    newPart = newPart with { Text = newPart.Text + " " };
                if (parts.Length == 0 || parts[partsIndexUpToCursor - 1].Type is CommandLineParser.CommandLinePart.PartType.Whitespace or CommandLineParser.CommandLinePart.PartType.Operator)
                    parts = parts[..partsIndexUpToCursor]
                        .Append(newPart)
                        .Concat(parts[partsIndexUpToCursor..])
                        .ToArray();
                else
                    parts[partsIndexUpToCursor-1] = newPart;
                var oldSize = _commandLine.Length;
                _commandLine = string.Join("", parts.Select(p => p.Text));
                Prompt.CursorPosition += _commandLine.Length - oldSize; 
            }
            RequestRedraw();
        }
        _selection = NoneSelected;
    }

    private void UpdateSuggestionsAndSelection()
    {
        if (Prompt.CursorPosition == _commandLine.Length || _commandLine[Prompt.CursorPosition] == ' ')
            _suggestions = Suggestor.SuggestionsForPrompt(_commandLine[..Prompt.CursorPosition]);
        else
            _suggestions = [];
        if (_selection >= _suggestions.Length)
            _selection = _suggestions.Length > 0 ? 0 : SelectionNotShown;
        else if (_selection < MinSelectionValue)
            _selection = _suggestions.Length - 1;
    }
    
    private void RenderPromptAndSuggestionsIfNeeded()
    {
        if (!_needsRedraw) return;
        _needsRedraw = false;
        UpdateSuggestionsAndSelection();

        var aiPrompt = _commandLine.StartsWith(Configuration.Instance.AIPromptPrefix);
        var top = Prompt.RenderPrompt(_commandLine, _selection >= 0 && !aiPrompt ? _suggestions[_selection].Text : null);
        if (_suggestions.Length > 0 && _selection != SelectionNotShown && !(_suggestionsHidden && Configuration.Instance.OnlyShowSuggestionsWhenArrowKeyIsPressed && !(aiPrompt && _commandLine[Configuration.Instance.AIPromptPrefix.Length..].Trim().Length > 0)))
        {
            _didShowSuggestions = true;
            _suggestionsHidden = false;
            SuggestionConsoleViewer.ShowSuggestions(top, _suggestions, _commandLine, _selection);
        }
        else if (_didShowSuggestions)
            SuggestionConsoleViewer.Clear(top);
    }

    private void ConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        SetupRunCommand();
        Environment.Exit(0);
    }

    private void SetupRunCommand(string command = "")
    {
        var top = Prompt.RenderPrompt(_commandLine);
        if (_didShowSuggestions)
            SuggestionConsoleViewer.Clear(top);
        BufferedConsole.Flush();
        Console.WriteLine();
        _outputCommand.WriteAllText(command);
        History.AddCommandToHistory(command, _aiPrompt);
    }
}