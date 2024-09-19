using System.Reflection;
using NiceIO;

namespace boltprompt;

public static class KnownCommands
{
    private static readonly Dictionary<string, Task<CommandInfo>> AllKnownCommands;

    public delegate void UpdateCommandInfo(CommandInfo ci);

    public static event UpdateCommandInfo? CommandInfoLoaded;
    
    static List<ICommandInfoSupplier> commandSuppliers;
    
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
        Paths.GeneratedCommandsDir.CreateDirectory();
        var path = Paths.GeneratedCommandsDir.Combine($"{command}.json");
        var ci = GetPendingCommandInfo(command);
        CommandInfoLoaded?.Invoke(ci);
        foreach (var commandSupplier in commandSuppliers.Where(commandSupplier => commandSupplier.CanHandle(command)))
        {
            var newCi = await commandSupplier.GetCommandInfoForCommand(command);
            if (newCi == null) continue;
            path.WriteAllText(newCi.Serialize());
            CommandInfoLoaded?.Invoke(newCi);
            return newCi;
        }
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
        
        if (!commandSuppliers.Any(commandSupplier => commandSupplier.CanHandle(command))) return null;

        AllKnownCommands[command] = CreateAndCacheCommandInfo(command);
        return null;
    }
    
    static KnownCommands()
    {
        AllKnownCommands = new Dictionary<string, Task<CommandInfo>>
        {
            ["ls"] = Task.FromResult(CommandInfo.Ls)
        };
        LoadCommandsFromPath(Paths.BuiltInCommandsDir);
        LoadCommandsFromPath(Paths.GeneratedCommandsDir);

        var commandInfoSupplierTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetInterfaces().Contains(typeof(ICommandInfoSupplier)));
        commandSuppliers = commandInfoSupplierTypes
            .Select(t => (ICommandInfoSupplier)Activator.CreateInstance(t)!)
            .OrderBy(sup => sup.Order)
            .ToList();
        return;

        void LoadCommandsFromPath(NPath path)
        {
            if (!path.DirectoryExists()) return;
            foreach (var file in path.Files("*.json", true))
                AllKnownCommands[file.FileNameWithoutExtension] = LoadCachedCommandInfo(file);
        }
    }
}