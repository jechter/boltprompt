using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;

namespace boltprompt;

static class CustomArguments
{
    private static readonly Dictionary<string, Task<string>> CustomArgumentCache = new();

    public static event Action CustomArgumentsLoaded = () => {};

    private static async Task<string> GetAsync(string command)
    {
        var commandSplit = Suggestor.SplitCommandIntoWords(command);
        var result = await Cli.Wrap(commandSplit[0]).WithArguments(commandSplit.Skip(1).Select(s => s.Trim('"'))).WithValidation(CommandResultValidation.None).ExecuteBufferedAsync();
        return result.StandardOutput; 
    }

    static Suggestion[] ParseOutput(string output, CommandInfo.Argument argument)
    {
        var argDescription = string.IsNullOrEmpty(argument.Description) ? argument.Name : argument.Description;
        if (!string.IsNullOrEmpty(argument.CustomCommandRegex)) 
        {
            var matches = Regex.Matches(output, argument.CustomCommandRegex);

            return matches.Select(m => new Suggestion(m.Groups.TryGetValue("suggestion", out var suggestionGroup) ? suggestionGroup.Value : m.Groups[1].Value)
                { Description = m.Groups.TryGetValue("description", out var descriptionGroup) ? descriptionGroup.Value : m.Groups.Count > 2 ? m.Groups[2].Value : argDescription }).ToArray();
        }

        var lines = output.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        return lines.Select(line => new Suggestion(line) { Description = argDescription }).ToArray();
    }
    
    public static Suggestion[] Get(CommandInfo.Argument argument)
    {
        if (argument.CustomCommand == null)
            return [];
        
        if (CustomArgumentCache.TryGetValue(argument.CustomCommand, out var argumentsTask))
            return argumentsTask.IsCompleted ? ParseOutput(argumentsTask.Result, argument) : [];

        CustomArgumentCache[argument.CustomCommand] = GetAsync(argument.CustomCommand);
        CustomArgumentCache[argument.CustomCommand].ContinueWith(_ => CustomArgumentsLoaded.Invoke());
        return [];
    }
}