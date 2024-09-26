using System.Text.Json;
using System.Text.Json.Serialization;

namespace boltprompt;

public static class History
{
    public record Command(string Commandline)
    {
        [JsonInclude]
        public string? AIPrompt;
    }
    
    private static Command[]? _commands; 
    
    public static void AddCommandToHistory(string commandLine, string? aiPrompt)
    {
        if (string.IsNullOrEmpty(commandLine)) return;
        var command = new Command(commandLine) { AIPrompt = aiPrompt };
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
                _commands = JsonSerializer.Deserialize<Command[]>(Paths.History.ReadAllText()) ?? [];
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
}