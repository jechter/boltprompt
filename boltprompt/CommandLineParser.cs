using NiceIO;

namespace boltprompt;

public static class CommandLineParser
{
    private static readonly char[] ShellOperators = ['>', '<', '|', '&', ';']; 

    public record CommandLinePart(string Text)
    {
        public enum PartType
        {
            Command,
            Variable,
            Argument,
            Operator,
            Whitespace,
        }

        public PartType Type;
        public CommandInfo.Argument? Argument;
    }
    public class ArgumentParsingState
    {
        public ArgumentParsingState(CommandInfo ci, CommandInfo.ArgumentGroup[] groups)
        {
            CommandInfo = ci;
            Groups = groups;
            LoadEligibleArguments();
        }

        public readonly CommandInfo CommandInfo;
        public readonly CommandInfo.ArgumentGroup[] Groups;
        public List<(CommandInfo.Argument argument, int groupIndex)> EligibleArguments = [];
        public int MinGroupIndex;
        public int MaxGroupIndex;

        public void LoadEligibleArguments(int startIndex = 0)
        {
            var groupIndex = startIndex;
            while (Groups.Length > groupIndex)
            {
                var group = Groups[groupIndex];
                EligibleArguments.AddRange(group.Arguments.Select(a => (a, groupIndex)));
                if (!group.Optional)
                    break;
                groupIndex++;
            }

            if (groupIndex > MaxGroupIndex)
                MaxGroupIndex = groupIndex;
        }
    }

    public static IEnumerable<(CommandInfo.Argument argument, int groupIndex, int depth)> GetEligibleArgumentsForState(List<ArgumentParsingState> parsingState, int depth = 1)
    {
        if (depth > parsingState.Count)
            yield break;
        
        var currentState = parsingState[^depth];
        foreach (var argToMatch in currentState.EligibleArguments)
        {
            if (argToMatch.argument is { Type: CommandInfo.ArgumentType.CustomArgument, CustomArgumentTemplate: not null })
            {
                var template = CustomArguments.LookupTemplate(argToMatch.argument, currentState.CommandInfo);
                if (template.Arguments is { Length: > 0 })
                {
                    foreach (var templateArg in template.Arguments)
                        yield return (templateArg, argToMatch.groupIndex, depth);
                    continue;
                }
            }
            yield return (argToMatch.argument, argToMatch.groupIndex, depth);
        }

        if (currentState.MaxGroupIndex != currentState.Groups.Length) yield break;
        
        foreach (var deepArg in GetEligibleArgumentsForState(parsingState, depth + 1))
            yield return deepArg;
    }

    private static CommandInfo.Argument? ParseArgument(string arg, List<ArgumentParsingState> parsingState)
    {
        foreach (var (argument, groupIndex, depth) in GetEligibleArgumentsForState(parsingState))
        {
            if (!CanMatchArgument(arg, argument, parsingState.Last().CommandInfo)) continue;
            
            parsingState.RemoveRange(parsingState.Count + 1 - depth, depth - 1);
            var currentState = parsingState.Last();
            currentState.MinGroupIndex = groupIndex;
            if (!currentState.Groups[groupIndex].Optional)
                currentState.LoadEligibleArguments(groupIndex + 1);
            if (currentState.Groups[groupIndex].DontAllowMultiple)
                currentState.MinGroupIndex = groupIndex + 1;
            currentState.EligibleArguments = currentState.EligibleArguments.Where(a => a.groupIndex >= currentState.MinGroupIndex && (a.argument != argument || a.argument.Repeat)).ToList();
                
            if (argument.Arguments != null)
                parsingState.Add(new (currentState.CommandInfo, argument.Arguments));
                
            return argument;
        }
       
        return null;
    }

    private static bool CanMatchExtensions(string arg,string[]? extensions)
    {
        if (!(extensions?.Length > 0)) return true;
        NPath npath = arg;
        return npath.IsRoot || npath.HasExtension(extensions);
    }
    
    private static bool CanMatchArgument(string arg, CommandInfo.Argument argToMatch, CommandInfo ci) => argToMatch.Type switch
    {
        CommandInfo.ArgumentType.Keyword => argToMatch.AllNames.Any(a => a == arg),
        CommandInfo.ArgumentType.Flag => argToMatch.AllNames.Any(a => arg.StartsWith($"-{a}")),
        CommandInfo.ArgumentType.File => CanMatchExtensions(arg, argToMatch.Extensions),
        CommandInfo.ArgumentType.CustomArgument => argToMatch.CustomArgumentTemplate != null && CustomArguments.Match(arg, argToMatch, ci),
        CommandInfo.ArgumentType.FileSystemEntry or CommandInfo.ArgumentType.Directory
            or CommandInfo.ArgumentType.Command or CommandInfo.ArgumentType.CommandName
            or CommandInfo.ArgumentType.String or CommandInfo.ArgumentType.Unknown => true,
        _ => throw new ArgumentOutOfRangeException()
    };

    public static IEnumerable<CommandLinePart> ParseCommandLine(string commandline, List<ArgumentParsingState>? parsingStateOut = null)
    {
        if (Prompt.IsAIPrompt(commandline))
        {
            yield return new(commandline);
            yield break;
        }
        
        var pos = 0;
        var lastPartType = CommandLinePart.PartType.Whitespace;
        var parsingState = parsingStateOut ?? new List<ArgumentParsingState>();
        while (pos < commandline.Length)
        {
            var nextPos = pos;
            while (nextPos < commandline.Length && IsWhiteSpace(nextPos)) nextPos++;
            
            if (nextPos != pos)
            {
                yield return new (commandline[pos..nextPos]) { Type = CommandLinePart.PartType.Whitespace };
                pos = nextPos;
            }
            
            if (pos == commandline.Length) break;
            
            if (ShellOperators.Contains(commandline[pos]))
            {
                yield return new (commandline[pos].ToString()) { Type = CommandLinePart.PartType.Operator };
                lastPartType = CommandLinePart.PartType.Operator;
                pos++;
            }
                
            nextPos = pos;
            
            if (pos == commandline.Length) break;

            if (commandline[nextPos] == '"')
            {
                nextPos++;
                while (nextPos < commandline.Length && commandline[nextPos] != '"') nextPos++;
                if (nextPos < commandline.Length)
                    nextPos++;
            }
            else
                while (nextPos < commandline.Length && !IsWhiteSpace(nextPos) && !ShellOperators.Contains(commandline[nextPos])) nextPos++;

            if (nextPos != pos)
            {
                var part = new CommandLinePart(commandline[pos..nextPos])
                {
                    Type = lastPartType switch
                    {
                        CommandLinePart.PartType.Command => CommandLinePart.PartType.Argument,
                        CommandLinePart.PartType.Argument => CommandLinePart.PartType.Argument,
                        CommandLinePart.PartType.Operator => CommandLinePart.PartType.Command,
                        CommandLinePart.PartType.Whitespace => CommandLinePart.PartType.Command,
                        CommandLinePart.PartType.Variable => CommandLinePart.PartType.Command,
                        _ => throw new ArgumentOutOfRangeException()
                    }
                };

                // Variable assignment
                if (part.Type == CommandLinePart.PartType.Command && part.Text.Contains('='))
                {
                    var equalsPos = part.Text.IndexOf('=');
                    var variableName = part.Text[..equalsPos];
                    yield return new (variableName) { Type = CommandLinePart.PartType.Variable };
                    yield return new ("=") { Type = CommandLinePart.PartType.Operator };
                    part = new (part.Text[(equalsPos + 1)..])
                    {
                        Type = CommandLinePart.PartType.Argument, 
                        Argument = new ("value")
                        {
                            Type = CommandInfo.ArgumentType.Unknown,
                            Description = $"New value for {variableName}" 
                        }
                    };
                    parsingState.Clear();
                    parsingState.Add(new (CommandInfo.DefaultCommand, [new([part.Argument])]));
                    lastPartType = CommandLinePart.PartType.Variable;
                }
                else
                {
                    lastPartType = part.Type;
                    switch (part.Type)
                    {
                        case CommandLinePart.PartType.Command:
                            LoadCommandPart(part);
                            break;
                        case CommandLinePart.PartType.Argument:
                        {
                            var parsedArgument = ParseArgument(part.Text, parsingState);
                            if (parsedArgument != null)
                            {
                                part.Argument = parsedArgument;
                                if (parsedArgument.Type == CommandInfo.ArgumentType.Command)
                                    LoadCommandPart(part);
                            }

                            break;
                        }
                    }
                }

                yield return part;
                pos = nextPos;
            }
        }

        yield break;

        bool IsWhiteSpace(int index)
        {
            if (!char.IsWhiteSpace(commandline[index]))
                return false;
            return index == 0 || commandline[index - 1] != '\\';
        }


        void LoadCommandPart(CommandLinePart part)
        {
            parsingState.Clear();
            NPath commandPath = part.Text;
            if (commandPath.IsRoot) // this throws trying to get a file name otherwise.
                return;
            
            var createCommandInfo = commandPath.FileExists() || Suggestor.ExecutablesInPathEnvironment.Any(s => commandPath.FileName == s.Text.Trim());
            var commandInfo = KnownCommands.GetCommand(commandPath.FileName, createCommandInfo);

            if (commandInfo?.Arguments != null)
                parsingState.Add(new (commandInfo, commandInfo.Arguments));
        }
    }
    
}