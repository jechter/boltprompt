using NiceIO;

namespace Shelper;

public static class KnownCommands
{
    public static Dictionary<string, CommandInfo> All => s_KnownCommands;
    private static Dictionary<string, CommandInfo> s_KnownCommands;
    
    public static CommandInfo? GetCommand(string command)
    {
        if (s_KnownCommands.TryGetValue(command, out var ci))
            return ci;
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        var path = commandDir.Combine($"{command}.json");
        var gptTask = GPTCommandInfoSupplier.GetCommandInfoForCommand2(command);
        ci = gptTask.GetAwaiter().GetResult();
        s_KnownCommands[command] = ci;
        path.WriteAllText(ci.Serialize());
        return ci;
    }
    
    static KnownCommands()
    {
        s_KnownCommands = new Dictionary<string, CommandInfo>();
        s_KnownCommands["ls"] = CommandInfo.ls;
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        if (commandDir.DirectoryExists())
        {
            foreach (var file in commandDir.Files("*.json", true))
                s_KnownCommands[file.FileNameWithoutExtension] = CommandInfo.Deserialize(file.ReadAllText());
        }
        else
        {
            commandDir.CreateDirectory();
            foreach (var cmd in s_KnownCommands)
            {
                var path = commandDir.Combine($"{cmd.Key}.json");
                path.WriteAllText(cmd.Value.Serialize());
            }
        }
    }
}