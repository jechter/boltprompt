using System.Text.Json;
using System.Text.Json.Serialization;
using NiceIO;

namespace boltprompt;

public static class History
{
    public enum AIRequestType
    {
        Prompt,    
        Question
    }
    
    public record Command(string Commandline)
    {
        [JsonInclude]
        public AIRequestType? AIRequestType;
        [JsonInclude]
        public string? AIPrompt;
        [JsonInclude]
        public string? WorkingDirectory;
        [JsonInclude]
        public string? TerminalSession;
        [JsonInclude] 
        public bool CommandHasRelativePaths;
        [JsonInclude] 
        public DateTime TimeStamp;

        private CommandLineParser.CommandLinePart[]? _commandLineParts;

        [JsonIgnore]
        public CommandLineParser.CommandLinePart[] ParsedCommandLine =>
            _commandLineParts ??= CommandLineParser.ParseCommandLine(Commandline).ToArray();
    }
    
    private static Command[]? _commands;

    
    internal static void PurgeHistoryIfNeeded(int maxHistoryLength, TimeSpan timeToMergeOldHistory)
    {
        if (_commands == null)
            return;
        if (_commands.Length < maxHistoryLength)
            return;
        
        var now = DateTime.UtcNow;
        var oldHistory = _commands.Where(c => now - c.TimeStamp > timeToMergeOldHistory).DistinctBy(c => c.Commandline).ToArray();
        var newHistory = _commands.Where(c => now - c.TimeStamp <= timeToMergeOldHistory);
        _commands = oldHistory.Concat(newHistory).ToArray();
        if (_commands.Length < maxHistoryLength)
            return;
        
        _commands = _commands.OrderBy(c => c.TimeStamp).TakeLast(maxHistoryLength).ToArray();
    }
    
    public static void AddCommandToHistory(string commandLine, string? aiPrompt)
    {
        if (string.IsNullOrEmpty(commandLine)) return;
        commandLine = commandLine.Trim();
        var parsedCommand = CommandLineParser.ParseCommandLine(commandLine);
        var commandHasRelativePaths = parsedCommand.Any(part => 
                part is { Type: CommandLineParser.CommandLinePart.PartType.Argument, Argument.Type: CommandInfo.ArgumentType.File or CommandInfo.ArgumentType.Directory or CommandInfo.ArgumentType.FileSystemEntry }
                && Suggestor.UnescapeFileName(part.Text).IsRelative);
        var command = new Command(commandLine)
        {
            AIPrompt = aiPrompt, 
            AIRequestType = aiPrompt != null ? commandLine.StartsWith(Configuration.Instance.AIQuestionPrefix) ? AIRequestType.Question : AIRequestType.Prompt : null,
            WorkingDirectory = NPath.CurrentDirectory.ToString(), 
            CommandHasRelativePaths = commandHasRelativePaths,
            TerminalSession = TerminalUtility.TerminalSession,
            TimeStamp = DateTime.UtcNow,
        };
        _commands = null; // Force reload from disk, so we don't overwrite changes from other parallel boltprompt processes.
        _commands = Commands.Where(c => c.Commandline.Trim() != commandLine || c.TerminalSession != command.TerminalSession).Append(command).ToArray();
        PurgeHistoryIfNeeded(4096, TimeSpan.FromDays(7));
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