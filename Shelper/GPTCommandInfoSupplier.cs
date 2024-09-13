using CliWrap;
using CliWrap.Buffered;

namespace Shelper;

public class GptCommandInfoSupplier : ICommandInfoSupplier
{
    public static async Task<CommandInfo> GetCommandInfoForCommandOld(string command)
    {
        var commandResult = await Cli.Wrap("man")
            .WithArguments(command)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        var gptPromptPrefix = $"We want to learn about the `{command}` command line tool.";

        if (commandResult.ExitCode == 0)
        {
            var manpage = commandResult.StandardOutput;
            if (manpage.Length < 32 * 1024)
            {
                gptPromptPrefix += $"""

                                    This is the man page for `{command}`:
                                    {manpage}
                                    """;
            }
        }
    
        var result = new CommandInfo
        {
            Name = command,
            Description = await ChatGptClient.GetReply(gptPromptPrefix, $"Give a one-line description of what the `{command}` command does. Don't mention the command in the description.")
        };
        var argumentsText = await ChatGptClient.GetReply(gptPromptPrefix, $"List all the command line arguments the `{command}` command accepts. List one argument per line, just the arguments, no extra text.");
        var sourceArguments = argumentsText.Split("\n");
        var arguments = new List<CommandInfo.Argument>();
        foreach (var sourceArgument in sourceArguments)
        {
            var argLine = sourceArgument;
            if (argLine.StartsWith("- "))
                argLine = argLine[2..];
            var argSplit = argLine.Split('`');
            var arg = argSplit.Length <= 1 ? argLine.Trim() : argSplit[1];
            var type = arg.StartsWith('-') && arg.Length == 2
                ? CommandInfo.ArgumentType.Flag
                : CommandInfo.ArgumentType.Keyword;
                
            arguments.Add(new CommandInfo.Argument(type == CommandInfo.ArgumentType.Flag ? arg[1..] : arg)
            {
                Optional = await ChatGptClient.GetBooleanReply(gptPromptPrefix, $"Is the `{arg}` argument optional?"),
                Repeat = await ChatGptClient.GetBooleanReply(gptPromptPrefix, $"Can the `{arg}` argument appear more than once on the command line?"),
                Description = await ChatGptClient.GetReply(gptPromptPrefix, $"Give a one-line description of what the `{arg}` argument command does. Don't mention the command or argument in the description."),
                Type = type,
            });
        }
    
        if (await ChatGptClient.GetBooleanReply(gptPromptPrefix,
                $"Does the `{command}` command take any path names as arguments?"))
        {
            arguments.Add(new CommandInfo.Argument("pathname")
            {
                Type = CommandInfo.ArgumentType.FileSystemEntry,
                Description = await ChatGptClient.GetReply(gptPromptPrefix, $"Give a one-line description of what the path argument passed to the {command} command does. Don't mention the command or argument in the description."),
                Repeat = await ChatGptClient.GetBooleanReply(gptPromptPrefix, $"Does the `{command}` command take more than one path name as arguments?"),
                Optional = await ChatGptClient.GetBooleanReply(gptPromptPrefix, $"Is passing a path name argument to `{command}` optional?"),
            });
        }
    
        
    
        result.Arguments = [arguments.ToArray()];        
        result.Comment = "Written by artificial stupidity.";
        return result;
    }

    public int Order => 2;

    public bool CanHandle(string command)
    {
        return ChatGptClient.IsAvailable;
    }

    public async Task<CommandInfo?> GetCommandInfoForCommand(string command)
    {
        var commandResult = await Cli.Wrap("man")
            .WithArguments(command)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        var manPageMessage = "";

        if (commandResult.ExitCode == 0)
        {
            var manpage = commandResult.StandardOutput;
            if (manpage.Length < 16 * 1024)
            {
                manPageMessage += $"""

                                   This is the man page for `{command}`:
                                   {manpage}
                                   """;
            }
        }
        
        var gptPromptPrefix = $$"""
                                We are using a json format to describe possible arguments for command line tools, for the purpose of displaying suggestions in a CLI auto-complete tool. 
                                The json format has the following keys:

                                "Name" (string): the name of the command.
                                "Description" (string): a one-line description of what the command does.
                                "Arguments" (array of arrays of Argument objects, optional): The arguments the command takes, grouped into sub-arrays to allow specifying an order of different argument types. For example a command could have one array of Arguments for flags, and another one for pathname arguments to specify that flags must come before file names. Sub-arrays should be sorted by how commonly an argument is used, with the most common ones coming first.

                                The `Argument` object has the following keys:

                                "Name" (string): the name of the argument. This is used to look up either a matching ArgumentGroup or ArgumentDescription to define the argument.
                                "Optional" (boolean, optional): whether the argument is optional. Default is true.
                                "Repeat" (boolean, optional): whether the can be specified on the command line multiple times. Default is false.
                                "Description" (string): a one-line description of what the argument does.
                                "Type" (string): the type of the argument. Default is "Keyword". Must be one of: 
                                    "Keyword": A specific keyword, as specified in the "Values" key.
                                    "Flag": Like a keyword, but a single character, which can be combined with other flags into a single argument, prefixed by a `-` character.
                                    "FileSystemEntry": A file system path.
                                    "Directory": A file system path, which must point to a directory.
                                    "File": A file system path, which must point to a file.
                                    "Command": The name of another command.
                                    "String": Any other string.
                                "Values" (array of strings, optional): Only used if "Type" is "Keyword" or "Flag". A list of words (or in the case of "Flag", characters) to use for this argument on the command line. Can have only one entry, or multiple, if the command has different aliases for the same argument.
                                "Arguments" (array of Argument objects, optional): If this argument has arguments itself, these can be specified here.

                                The json for the `ls` command for example looks like this:

                                ```json
                                {{CommandInfo.Ls.Serialize()}}
                                ```

                                Can you write a json description for the `{{command}}` command? 

                                {{manPageMessage}}

                                Please reply with only the json.
                                """;
        var result = await ChatGptClient.GetReply(gptPromptPrefix);
        result = string.Join('\n', result.Split('\n').Where(l => !l.StartsWith("```")));
        var ci = CommandInfo.Deserialize(result) ?? throw new InvalidDataException("Could not parse GPT output");
        ci.Comment = "Written by artificial stupidity.";
        return ci;
    }
}