using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;

namespace boltprompt;

public class GptCommandInfoSupplier : ICommandInfoSupplier
{
    public int Order => 2;

    public bool CanHandle(string command)
    {
        Logger.Log(Logger.Gpt, $"CanHandle: {command}");
        return AIService.Available;
    }

    private static CommandInfo.Argument CleanupArgument(CommandInfo.Argument argument) 
        => argument.Type == CommandInfo.ArgumentType.Flag
        ? argument with { Name = argument.Name.Trim(' ', '-', '/')[..1] }
        : argument;

    private static CommandInfo.ArgumentGroup CleanupArgumentGroup(CommandInfo.ArgumentGroup ag) =>
        ag with { Arguments = ag.Arguments.Select(CleanupArgument).ToArray() }; 
    
    private static CommandInfo Cleanup(CommandInfo commandInfo) => 
        commandInfo with
        {
            Comment = "Written by artificial stupidity.", 
            Arguments = commandInfo.Arguments?.Select(CleanupArgumentGroup).ToArray()
        };

    public async Task<CommandInfo?> GetCommandInfoForCommand(string command)
    {
        Logger.Log(Logger.Gpt, $"GetCommandInfoForCommand: {command}");

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
                                We need descriptions of all the parameters accepted by command line tools, for the purpose of displaying suggestions in a CLI auto-complete tool.
                                
                                As an example, here's a description of the `ls` command: 
                                
                                {{CommandInfo.Ls.Serialize()}} 

                                Can you generate a description for the `{{command}}` command? 

                                {{manPageMessage}}

                                Call the 'ProvideCommandInfo' function and pass the relevant description of the command and its arguments in the 'suggestion' argument.
                                """;

        try
        {
            var info = CommandInfo.DefaultCommand;
            await AIService.RequestWithFunctions(gptPromptPrefix, default, ProvideCommandInfo);

            return info;
            
            [Description("function to invoke with command info")]
            bool ProvideCommandInfo([Description("info about the command and its arguments")]CommandInfo suggestion)
            {
                Logger.Log(Logger.Gpt,$"Received AI CommandInfo: {suggestion}");
                info = Cleanup(suggestion);
                return true;
            }
        }
        catch (Exception e)
        {
            Logger.Log(Logger.Gpt, $"Caught: {e}");
            throw;
        }
    }
}