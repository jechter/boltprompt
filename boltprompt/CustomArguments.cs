using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;

namespace boltprompt;

static class CustomArguments
{
    private static readonly Dictionary<string, Task<string>> OutputCache = new();
    private static readonly Dictionary<(string output, string? regex), Suggestion[]> ParsingCache = new();

    public static event Action CustomArgumentsLoaded = () => {};

    private static async Task<string> GetAsync(string command)
    {
        var commandSplit = Suggestor.SplitCommandIntoWords(command);
        var result = await Cli.Wrap(commandSplit[0]).WithArguments(commandSplit.Skip(1).Select(s => s.Trim('"'))).WithValidation(CommandResultValidation.None).ExecuteBufferedAsync();
        return result.StandardOutput; 
    }

    static Suggestion[] ParseOutput(string output, CommandInfo.Argument argument, CommandInfo.CustomArgumentTemplate template)
    {
        if (ParsingCache.TryGetValue((output, template.Regex), out var result))
            return result;
            
        var argDescription = string.IsNullOrEmpty(argument.Description) ? argument.Name : argument.Description;
        if (!string.IsNullOrEmpty(template.Regex))
        {
            var matches = Regex.Matches(output, template.Regex, RegexOptions.Multiline);

            result = matches.Select(m =>
                new Suggestion(m.Groups.TryGetValue("suggestion", out var suggestionGroup)
                    ? suggestionGroup.Value
                    : m.Groups[1].Value)
                {
                    Description = m.Groups.TryGetValue("description", out var descriptionGroup)
                        ? descriptionGroup.Value
                        : m.Groups.Count > 2
                            ? m.Groups[2].Value
                            : argDescription,
                    Icon = template.Icon
                }).ToArray();
        }
        else
        {
            var lines = output.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            result = lines
                .Select(line => new Suggestion(line)
                {
                    Description = argDescription, 
                    Icon = template.Icon
                })
                .ToArray();
        }

        ParsingCache[(output, template.Regex)] = result;
        return result;
    }

    public static CommandInfo.CustomArgumentTemplate LookupTemplate(CommandInfo.Argument argument, CommandInfo ci)
        => ci.CustomArgumentTemplates?.Single(t => t.Name == argument.CustomArgumentTemplate) 
        ?? throw new InvalidDataException($"No custom argument template matching {argument.CustomArgumentTemplate} was found");

    public static bool Match(string argument, CommandInfo.Argument argToMatch, CommandInfo ci)
    {
        var template = LookupTemplate(argToMatch, ci);
        return template.StrictMatching == false || Get(argToMatch, ci, null, null).Any(a => a.Text == argument);
    }

    public static Suggestion[] Get(CommandInfo.Argument argument, CommandInfo ci, CommandLineParser.CommandLinePart[]? parts, string? lastParam)
    {
        var template = LookupTemplate(argument, ci);
        if (template.Command == null)
            return [];
        
        var command = template.Command;

        const string argParam = "{ARG[^";
        if (parts != null && lastParam != null)
        {
            while (command.Contains(argParam))
            {
                var index = command.IndexOf(argParam, StringComparison.Ordinal);
                var endIndex = command.IndexOf("]}", index, StringComparison.Ordinal);
                var numStr = command[(index + argParam.Length)..endIndex];
                var num = int.Parse(numStr);
                var param = num == 0
                    ? lastParam
                    : parts.Where(p => p.Type == CommandLineParser.CommandLinePart.PartType.Argument)
                        .ToArray()[^num].Text;
                command = command.Replace($"{{ARG[^{numStr}]}}", param);
            }
        }

        if (OutputCache.TryGetValue(command, out var argumentsTask))
            return argumentsTask.IsCompleted ? ParseOutput(argumentsTask.Result, argument, template).Select(s => s with {Argument = argument}).ToArray() : [];

        OutputCache[command] = GetAsync(command);
        OutputCache[command].ContinueWith(_ => CustomArgumentsLoaded.Invoke());
        return [];
    }
}