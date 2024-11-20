using System.Text.Json;
using System.Text.Json.Serialization;
using NiceIO;

namespace boltprompt;

public static class History
{
    public record Command(string Commandline)
    {
        [JsonInclude]
        public string? AIPrompt;
        [JsonInclude]
        public string? WorkingDirectory;
        [JsonInclude]
        public string? TerminalSession;
        [JsonInclude] 
        public bool CommandHasRelativePaths;

        private CommandLineParser.CommandLinePart[]? _commandLineParts;

        [JsonIgnore]
        public CommandLineParser.CommandLinePart[] ParsedCommandLine =>
            _commandLineParts ??= CommandLineParser.ParseCommandLine(Commandline).ToArray();
    }
    
    private static Command[]? _commands; 
    
    public static void AddCommandToHistory(string commandLine, string? aiPrompt)
    {
        if (string.IsNullOrEmpty(commandLine)) return;
        var parsedCommand = CommandLineParser.ParseCommandLine(commandLine);
        var commandHasRelativePaths = parsedCommand.Any(part => 
                part is { Type: CommandLineParser.CommandLinePart.PartType.Argument, Argument.Type: CommandInfo.ArgumentType.File or CommandInfo.ArgumentType.Directory or CommandInfo.ArgumentType.FileSystemEntry }
                && Suggestor.UnescapeFileName(part.Text).IsRelative);
        var command = new Command(commandLine)
        {
            AIPrompt = aiPrompt, 
            WorkingDirectory = NPath.CurrentDirectory.ToString(), 
            CommandHasRelativePaths = commandHasRelativePaths,
            TerminalSession = TerminalUtility.TerminalSession
        };
        _commands = null; // Force reload from disk, so we don't overwrite changes from other parallel boltprompt processes.
        _commands = Commands.Where(c => c != command).Append(command).ToArray();
        Paths.History.WriteAllText(JsonSerializer.Serialize(_commands));
    }

    public static Command[] Commands
    {
        get
        {
            if (_commands != null) return _commands;
            _commands = [];
            if (!Paths.History.FileExists()) return _commands;
            try
            {
                var commandsFromFile = JsonSerializer.Deserialize<Command[]>(Paths.History.ReadAllText());
                if (commandsFromFile != null)
                {
                    var commandsFromOtherTerminals = commandsFromFile.Where(c => c.TerminalSession != TerminalUtility.TerminalSession).ToArray();
                    var commandsFromThisTerminal = commandsFromFile.Where(c => c.TerminalSession == TerminalUtility.TerminalSession).ToArray();

                    _commands = commandsFromOtherTerminals
                        .Where(c => commandsFromThisTerminal.All(c2 => c2.Commandline != c.Commandline))
                        .Concat(commandsFromThisTerminal)
                        .ToArray();
                }
            }
            catch (JsonException)
            {
                _commands = [];
            }

            return _commands;
        }
    }

    internal static void LoadTestHistory(string[] commands)
    {
        _commands = commands.Select(c => new Command(c)).ToArray();
    }
    
    internal static void LoadTestHistoryCommands(Command[] commands)
    {
        _commands = commands;
    }
}