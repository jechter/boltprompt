using NiceIO;

namespace Shelper;

public static class KnownCommands
{
    private static readonly Dictionary<string, Task<CommandInfo>> AllKnownCommands;

    public delegate void UpdateCommandInfo(CommandInfo ci);

    public static event UpdateCommandInfo? CommandInfoLoaded;

    internal static void AddCommandInfo(string command, CommandInfo ci)
    {
        AllKnownCommands[command] = Task.FromResult(ci);
    }
    
    static CommandInfo GetPendingCommandInfo(string command) => CommandInfo.DefaultCommand with
    {
        Name = command,
        Description = "Command description and parameter information pending..."
    };
    
    private static async Task<CommandInfo> CreateAndCacheCommandInfo(string command)
    {
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        var path = commandDir.Combine($"{command}.json");
        var ci = GetPendingCommandInfo(command);
        CommandInfoLoaded?.Invoke(ci);
        ci = await GptCommandInfoSupplier.GetCommandInfoForCommand2(command);
        path.WriteAllText(ci.Serialize());
        CommandInfoLoaded?.Invoke(ci);
        return ci;
    }
    
    private static async Task<CommandInfo> LoadCachedCommandInfo(NPath path)
    {
        var json = await File.ReadAllTextAsync(path.ToString());
        var ci = CommandInfo.Deserialize(json);
        if (ci == null)
            throw new InvalidDataException($"Could not load command info json for {path}");
        CommandInfoLoaded?.Invoke(ci);
        return ci;
    }
    
    public static CommandInfo? GetCommand(string command, bool createInfoIfNotAvailable)
    {
        if (AllKnownCommands.TryGetValue(command, out var ci))
            return ci.IsCompleted ? ci.Result : GetPendingCommandInfo(command);

        if (!createInfoIfNotAvailable) return null;

        if (!ChatGptClient.IsAvailable) return null;
        
        AllKnownCommands[command] = CreateAndCacheCommandInfo(command);
        return null;
    }
    
    static KnownCommands()
    {
        AllKnownCommands = new Dictionary<string, Task<CommandInfo>>
        {
            ["ls"] = Task.FromResult(CommandInfo.Ls)
        };
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        if (commandDir.DirectoryExists())
        {
            foreach (var file in commandDir.Files("*.json", true))
                AllKnownCommands[file.FileNameWithoutExtension] = LoadCachedCommandInfo(file);
        }
        else
        {
            commandDir.CreateDirectory();
            foreach (var cmd in AllKnownCommands)
            {
                var path = commandDir.Combine($"{cmd.Key}.json");
                path.WriteAllText(cmd.Value.Result.Serialize());
            }
        }
    }
}