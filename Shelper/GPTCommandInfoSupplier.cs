using CliWrap;
using CliWrap.Buffered;

namespace Shelper;

public static class GptCommandInfoSupplier
{
    public static async Task<CommandInfo> GetCommandInfoForCommand2(string command)
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

    public static async Task<string> GetCommandInfoForCommand(string command)
    {
    var commandResult = await Cli.Wrap("man")
      .WithArguments(command)
      .ExecuteBufferedAsync();
    var manpage = commandResult.StandardOutput;
    var gptPromptPrefix = $$"""
                              We are using a json format to describe possible arguments for command line tools, for the purpose of displaying suggestions in a CLI auto-complete tool. 
                              The json format has the following keys:
                              
                              "Name" (string): the name of the command.
                              "Description" (string): a one-line description of what the command does.
                              "Arguments" (array of Argument objects, optional): The arguments the command takes.
                              "ArgumentDescriptions" (array of ArgumentDescription objects, optional): Descriptions for the syntax for each argument.
                              "ArgumentGroups" (array of ArgumentGroup objects, optional): Groups of different arguments which can all come in one place on the command line.
                              
                              The `Argument` object has the following keys:
                              
                              "Name" (string): the name of the argument. This is used to look up either a matching ArgumentGroup or ArgumentDescription to define the argument.
                              "Optional" (boolean, optional): whether the argument is optional. Default is true.
                              "Repeat" (boolean, optional): whether the can be specified on the command line multiple times. Default is false.
                              
                              The `ArgumentDescription` object has the following keys:

                              "Name" (string): the name of the argument. Must match a name from an `Argument` object.
                              "Description" (string): a one-line description of what the argument does.
                              "Type" (string): the type of the argument. Default is "Keyword". Must be one of: 
                                  "Keyword": A specific keyword, as specified in the "Values" key.
                                  "FileSystemEntry": A file system path.
                                  "Directory": A file system path, which must point to a directory.
                                  "File": A file system path, which must point to a file.
                                  "Command": The name of another command.
                                  "String": Any other string.
                              "Values" (array of strings, optional): Only used if "Type" is "Keyword". A list of words to use for this argument on the command line. Can have only one entry, or multiple, if the command has different aliases for the same argument.
                              "Arguments" (array of Argument objects, optional): If this argument has arguments itself, these can be specified here.
                              
                              The `ArgumentGroup` object has the following keys:
                              
                              "Name" (string): the name of the argument. Must match a name from an `Argument` object.
                              "Arguments" (array of Argument objects): A list of arguments which can be used in this place.
                              
                              The json for the `ls` command for example looks like this:
                              
                              ```json
                              {{CommandInfo.ls.Serialize()}}
                              ```
                              
                              Can you write a json description for the `{{command}}` command? 
                              
                              This is the man page for `{{command}}`:
                              
                              ```
                              {{manpage}}
                              ```
                              
                              Please reply with only the json.
                              """;
        var result = await ChatGptClient.GetReply(gptPromptPrefix);
        return result;
    }
}