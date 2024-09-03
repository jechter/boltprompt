using NiceIO;

namespace Shelper;

public static class KnownCommands
{
    public static Dictionary<string, CommandInfo> All => AllKnownCommands;
    private static readonly Dictionary<string, CommandInfo> AllKnownCommands;
    
    public static CommandInfo GetCommand(string command)
    {
        if (AllKnownCommands.TryGetValue(command, out var ci))
            return ci;
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        var path = commandDir.Combine($"{command}.json");
        var gptTask = GptCommandInfoSupplier.GetCommandInfoForCommand(command);
        ci = gptTask.GetAwaiter().GetResult();
        AllKnownCommands[command] = ci;
        path.WriteAllText(ci.Serialize());
        return ci;
    }
    
    static KnownCommands()
    {
        AllKnownCommands = new Dictionary<string, CommandInfo>
        {
            ["ls"] = CommandInfo.Ls
        };
        NPath commandDir = "Commands";
        commandDir = commandDir.MakeAbsolute();
        if (commandDir.DirectoryExists())
        {
            foreach (var file in commandDir.Files("*.json", true))
            {
                var command = CommandInfo.Deserialize(file.ReadAllText());
                if (command != null)
                    AllKnownCommands[file.FileNameWithoutExtension] = command;
            }
        }
        else
        {
            commandDir.CreateDirectory();
            foreach (var cmd in AllKnownCommands)
            {
                var path = commandDir.Combine($"{cmd.Key}.json");
                path.WriteAllText(cmd.Value.Serialize());
            }
        }
    }
}