using System.Collections;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using Mono.Unix.Native;
using NiceIO;

namespace boltprompt;

public record Suggestion(string Text)
{
    public string? Icon;
    private string? _description;

    public string? Description
    {
        get => _description ?? SecondaryDescription;
        set => _description = value;
    }

    public virtual string? SecondaryDescription { get; set; }
}

public record FileSystemSuggestion(string Text) : Suggestion(Text)
{
    public override string? SecondaryDescription => FileDescriptions.GetFileDescription(Text);
}

public static partial class Suggestor
{
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
    
    private static Suggestion[] _executablesInPathEnvironment = [];
    private static readonly char[] ShellOperators = ['>', '<', '|', '&', ';']; 
    public static Suggestion[] ExecutablesInPathEnvironment => _executablesInPathEnvironment;
    private static readonly Dictionary<string, string> UserDirCache = new();

    static Suggestion? GetExecutableCommandInfo(string command) =>
        _executablesInPathEnvironment.FirstOrDefault(exe => exe.Text.Trim() == Path.GetFileName(command));

    public static void Init()
    {
        _executablesInPathEnvironment = FindExecutablesInPathEnvironment();
    }
    
    static Suggestor()
    {
        Init();
        KnownCommands.CommandInfoLoaded += ci =>
        {
            var sug = GetExecutableCommandInfo(ci.Name);
            if (sug != null)
                sug.Description = ci.Description;
        };
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

    static IEnumerable<(CommandInfo.Argument argument, int groupIndex, int depth)> GetEligibleArgumentsForState(List<ArgumentParsingState> parsingState, int depth = 1)
    {
        if (depth > parsingState.Count)
            yield break;
        
        var currentState = parsingState[^depth];
        foreach (var argToMatch in currentState.EligibleArguments)
            yield return (argToMatch.argument, argToMatch.groupIndex, depth);

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

    private static bool CanMatchArgument(string arg, CommandInfo.Argument argToMatch, CommandInfo ci) => argToMatch.Type switch
    {
        CommandInfo.ArgumentType.Keyword => argToMatch.AllNames.Any(a => a == arg),
        CommandInfo.ArgumentType.Flag => argToMatch.AllNames.Any(a => arg.StartsWith($"-{a}")),
        CommandInfo.ArgumentType.File => !(argToMatch.Extensions?.Length > 0) ||
                                         arg.ToNPath().HasExtension(argToMatch.Extensions),
        CommandInfo.ArgumentType.FileSystemEntry or CommandInfo.ArgumentType.Directory
            or CommandInfo.ArgumentType.Command or CommandInfo.ArgumentType.CommandName
            or CommandInfo.ArgumentType.String or CommandInfo.ArgumentType.CustomArgument 
            or CommandInfo.ArgumentType.Unknown => true,
        _ => throw new ArgumentOutOfRangeException()
    };

    public static IEnumerable<CommandLinePart> ParseCommandLine(string commandline, List<ArgumentParsingState>? parsingStateOut = null)
    {
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
                    var equalsPos = commandline.IndexOf('=');
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
            
            var createCommandInfo = commandPath.FileExists() || _executablesInPathEnvironment.Any(s => commandPath.FileName == s.Text.Trim());
            var commandInfo = KnownCommands.GetCommand(commandPath.FileName, createCommandInfo);

            if (commandInfo?.Arguments != null)
                parsingState.Add(new (commandInfo, commandInfo.Arguments));
        }
    }
    
    class SuggestionSorter : IComparer<Suggestion>
    {
        private readonly string[] _historyFilteredByCommandline;
        
        public SuggestionSorter(string commandline)
        {
            if (string.IsNullOrWhiteSpace(commandline))
                _historyFilteredByCommandline = History.Commands.Select(s => s.Commandline.Trim()).ToArray();
            else
            {
                var commandLineWords = commandline.Split(' ');
                var lastCommandLineWord = commandLineWords.Last();
                var commandLineWordPathComponents = lastCommandLineWord.Split('/');

                _historyFilteredByCommandline =
                    History.Commands.Where(s => s.Commandline.StartsWith(commandline)).Select(FilterHistoryEntryByCommandLine)
                        .ToArray();

                string FilterHistoryEntryByCommandLine(History.Command historyEntry)
                {
                    var historyEntryWords = historyEntry.Commandline.Split(' ');
                    var historyEntryCommandLineWord = historyEntryWords[commandLineWords.Length - 1];
                    var historyEntryPathComponents = historyEntryCommandLineWord.Split('/');
                    var filteredHistoryEntryPathComponents =
                        historyEntryPathComponents.Take(commandLineWordPathComponents.Length);
                    if (commandLineWordPathComponents.Length < historyEntryPathComponents.Length)
                        return string.Join('/', filteredHistoryEntryPathComponents) + "/";
                    return historyEntryCommandLineWord;
                }
            }
        }
        
        public int Compare(Suggestion? x, Suggestion? y)
        { 
            if (ReferenceEquals(x, y)) return 0;
            if (y is null) return 1;
            if (x is null) return -1;
            var xHistIndex = Array.LastIndexOf(_historyFilteredByCommandline, x.Text.Trim());
            var yHistIndex = Array.LastIndexOf(_historyFilteredByCommandline, y.Text.Trim());
            if (xHistIndex != yHistIndex) return xHistIndex - yHistIndex;

            if (x is FileSystemSuggestion && y is FileSystemSuggestion)
            {
                NPath pathX = UnescapeFileName(x.Text);
                NPath pathY = UnescapeFileName(y.Text);
                if (pathX.Parent != pathY.Parent)
                    return pathY.CompareTo(pathX);
                var xIsInvisible = pathX.FileName.StartsWith('.');
                var yIsInvisible = pathY.FileName.StartsWith('.');
                if (xIsInvisible != yIsInvisible)
                    return yIsInvisible.CompareTo(xIsInvisible);
                var xIsDir = pathX.DirectoryExists();
                var yIsDir = pathY.DirectoryExists();
                return xIsDir != yIsDir ? xIsDir.CompareTo(yIsDir) : pathY.CompareTo(pathX);
            }

            if (x is FileSystemSuggestion) return 1;
            if (y is FileSystemSuggestion) return -1;

            // We want "real arguments" before options
            if (x.Text.StartsWith('-') != y.Text.StartsWith('-'))
                return x.Text.StartsWith('-') ? -1 : 1;
            
            return string.Compare(y.Text, x.Text, StringComparison.Ordinal);
        }
    }
    
    static Suggestion[] SortSuggestionsByHistory(string commandline, IEnumerable<Suggestion> suggestions)
    {
        return string.IsNullOrWhiteSpace(commandline) ? suggestions.ToArray() : suggestions.OrderDescending(new SuggestionSorter(commandline)).ToArray();
    }

    public static string[] SplitCommandIntoWords(string currentCommand)
    {
        var result = new List<string>();
        var command = currentCommand;
        while (command.Length > 0)
        {
            var quoteIndex = command.IndexOf(" \"", StringComparison.Ordinal);
            if (quoteIndex != -1)
            {
                result.AddRange(CommandLineWordsRegex().Split(command[..quoteIndex]));
                var endQuoteIndex = command.IndexOf("\" ", quoteIndex + 2, StringComparison.Ordinal);
                if (endQuoteIndex != -1)
                {
                    result.Add(command[(quoteIndex+1)..(endQuoteIndex+1)]);
                    command = command[(endQuoteIndex+2)..];
                }
                else
                {
                    result.Add(command[(quoteIndex+1)..]);
                    command = "";
                }
            }
            else
            {
                result.AddRange(CommandLineWordsRegex().Split(command));
                command = "";
            }
        }
        return result.ToArray();
    }

    public static Suggestion[] SuggestionsForPrompt(string commandline)
    {
        if (commandline.StartsWith('@'))
        {
            var aiPrompt = commandline[1..];
            return AISuggestor.Suggest(aiPrompt)
                .Concat(
                    History.Commands.Where(h => (h.AIPrompt?.StartsWith(aiPrompt) ?? false) && h.AIPrompt.Length > aiPrompt.Length)
                        .Select(h => $"@{h.AIPrompt!}")
                        .Reverse()
                        .Distinct()
                        .Select(h => new Suggestion(h))
                )
                .ToArray();
        }

        var parsed = ParseCommandLine(commandline).ToArray();
        var currentPart = parsed.LastOrDefault(new CommandLinePart("") { Type = CommandLinePart.PartType.Command });
        var suggestions = currentPart.Type == CommandLinePart.PartType.Command
            ? SuggestCommand(currentPart.Text)
            : SuggestParameters(commandline).ToArray();
        if (currentPart.Text.StartsWith('$'))
            suggestions = SuggestEnvironmentVariables(currentPart.Text).Concat(suggestions).ToArray();
        return SortSuggestionsByHistory(commandline, suggestions);
    }

    private static IEnumerable<Suggestion> SuggestEnvironmentVariables(string currentWord)
    {
        var currentWordNoPrefix = currentWord[1..];
        var vars = Environment.GetEnvironmentVariables();
        vars["?"] = "Exit status of the most recently executed foreground pipeline.";
        vars["!"] = "Process ID of the job most recently placed into the background.";
        vars["$"] = "Process ID of the shell.";
        foreach (DictionaryEntry kvp in vars)
        {
            if (kvp.Key.ToString()?.StartsWith(currentWordNoPrefix) ?? false)
                yield return new($"${kvp.Key}") { Description = kvp.Value?.ToString() };
        }
    }

    private static string EscapeFileName(string fileName) => fileName.Replace(@"\", @"\\").Replace(" ", @"\ ");
    
    public static string UnescapeFileName(string fileName)
    {
       var unescaped = fileName.Replace(@"\ ", " ").Replace(@"\\", @"\");

       if (!unescaped.StartsWith('~')) return unescaped;
       
       var slashIndex = unescaped.IndexOf('/');
       if (slashIndex is -1 or 1)
           unescaped = unescaped.Replace("~", NPath.HomeDirectory.ToString());
       else
       {
           var userNameEscape = unescaped[..slashIndex];
           if (!UserDirCache.TryGetValue(userNameEscape, out var userDir))
           {
               var result = Cli.Wrap("sh").WithArguments($"-c \"echo {userNameEscape}\"").ExecuteBufferedAsync().GetAwaiter().GetResult();
               userDir = result.StandardOutput.Trim('\n', ' ');
               UserDirCache[userNameEscape] = userDir;
           }
           unescaped = unescaped.Replace(userNameEscape, userDir);
       }
       return unescaped;
    } 
    
    private static string NPathToSuggestionText(string prefix, NPath parent, NPath path)
        => $"{prefix}{EscapeFileName(path.RelativeTo(parent).ToString())}{(path.DirectoryExists() ? '/' : ' ')}";
    
    private static Suggestion[] SuggestFileSystemEntries(string currentArg, CommandInfo.ArgumentType type, string[]? extensions = null)
    {
        var dir = NPath.CurrentDirectory;
        var prefix = "";
        if (currentArg.Contains('/'))
        {
            prefix = currentArg[..(currentArg.LastIndexOf('/') + 1)];
            dir = UnescapeFileName(prefix.Replace("\\ ", " "));
        }
        
        if (!dir.DirectoryExists())
            return [];

        NPath[] contents = [];
        try
        {
            contents = type == CommandInfo.ArgumentType.Directory ? dir.Directories() : dir.Contents();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return (prefix == "" ? new []{dir}:[]).Concat(contents)
            .Where(fs => fs.DirectoryExists() ||
                 type switch
                 { 
                     CommandInfo.ArgumentType.CommandName => IsExecutable(fs) && !string.IsNullOrEmpty(prefix),
                     CommandInfo.ArgumentType.File => extensions == null || fs.HasExtension(extensions),
                     _ => true
                 })
            .Select(Suggestion (fs) => new FileSystemSuggestion(NPathToSuggestionText(prefix, dir, fs)) { Icon = fs.DirectoryExists()?"📁" : "📄"})
            .ToArray();
    }
    
    private static IEnumerable<Suggestion> SuggestParameters(string commandline)
    {
        var parsingState = new List<ArgumentParsingState>();
        var parts = ParseCommandLine(commandline, parsingState).ToArray();
        var lastParam = "";
        
        if (parts[^1].Type != CommandLinePart.PartType.Whitespace)
        {
            var commandLineToParse = string.Concat(parts[..^1].Select(p => p.Text));
            lastParam = parts[^1].Text;
            parsingState = [];
            _ = ParseCommandLine(commandLineToParse, parsingState).Count(); // We do Count here to force running the iterator to the end
        }
        
        var arguments = GetEligibleArgumentsForState(parsingState).Select(a => a.argument).ToArray();
        var allFlags = arguments.Where(a => a.Type == CommandInfo.ArgumentType.Flag).SelectMany(a => a.AllNames).SelectMany(a => a).ToArray();
        var hasMatch = false;
        foreach (var arg in arguments)
        {
            switch (arg.Type)
            {
                case CommandInfo.ArgumentType.Flag:
                    if (lastParam.Length == 0)
                    {
                        foreach (var v in arg.AllNames)
                            yield return new("-" + v) { Description = arg.Description };
                    }
                    else if (lastParam.StartsWith('-'))
                    {
                        if (lastParam[1..].Any(c => !allFlags.Contains(c)))
                            break;
                        
                        foreach (var v in arg.AllNames)
                            if (lastParam[^1] == v[0])
                                yield return new(lastParam) { Description = arg.Description };
                        foreach (var v in arg.AllNames)
                            if (!lastParam.Contains(v[0]) || arg.Repeat)
                                yield return new(lastParam + v) { Description = arg.Description };
                    }
                    break;
                case CommandInfo.ArgumentType.Keyword:
                    foreach (var v in arg.AllNames)
                    {
                        if (!v.StartsWith(lastParam)) continue;
                        yield return new(v) { Description = arg.Description };
                        hasMatch = true;
                    }

                    break;
                case CommandInfo.ArgumentType.CommandName:
                case CommandInfo.ArgumentType.Command:
                    foreach (var s in _executablesInPathEnvironment.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;
                    break;
                case CommandInfo.ArgumentType.FileSystemEntry:
                case CommandInfo.ArgumentType.Directory:
                case CommandInfo.ArgumentType.File:
                case CommandInfo.ArgumentType.Unknown:
                    foreach (var s in SuggestFileSystemEntries(lastParam, arg.Type, arg.Extensions))
                    {
                        if (s.Text.StartsWith(lastParam))
                        {
                            yield return s with { Description = arg.Description };
                            hasMatch = true;
                        }
                    }
                    // if we have no matching files, or if type is unknown (ie may not be a path at all), return a match for whatever was typed to allow creating new paths.
                    if (!hasMatch || arg.Type == CommandInfo.ArgumentType.Unknown)
                        yield return new (lastParam) { Description = string.IsNullOrEmpty(arg.Description) ? arg.Name : arg.Description };
                    break;
                case CommandInfo.ArgumentType.CustomArgument:
                    foreach (var s in CustomArguments.Get(arg, parsingState.Last().CommandInfo, parts, lastParam))
                    {
                        if (s.Text.StartsWith(lastParam))
                        {
                            yield return s;
                            hasMatch = true;
                        }
                    }
                    break;
                case CommandInfo.ArgumentType.String:
                    yield return new (lastParam) { Description = string.IsNullOrEmpty(arg.Description) ? arg.Name : arg.Description };
                    // We have no suggestions for generic strings
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    private static Suggestion[] SuggestCommand(string commandline)
    {
        if (string.IsNullOrEmpty(commandline))
            return History.Commands.Select(h => new Suggestion(h.Commandline)).ToArray();
        return _executablesInPathEnvironment
            .Concat(
                SuggestFileSystemEntries(commandline, CommandInfo.ArgumentType.CommandName)
                    .Select(sug => sug with { Description = KnownCommands.GetCommand(sug.Text.Split('/').Last().Trim(), false)?.Description ?? "" })
                )
            .Concat(History.Commands.Select(h => new Suggestion(h.Commandline)))
            .DistinctBy(s => s.Text)
            .Where(sug => sug.Text.StartsWith(commandline))
            .ToArray();
    }

    private static Suggestion[] FindExecutablesInPath(NPath path)
    {
        var executables = path.Files()
            .Where(IsExecutable);
        return executables.Select(ex => new Suggestion($"{ex.FileName} ")
            {
                Description = KnownCommands.GetCommand(ex.FileName, false)?.Description ?? ex.ToString()
            })
            .OrderBy(s => s.Text)
            .ToArray();
    }

    private static bool IsExecutable(NPath filePath)
    {
        if (Syscall.stat(filePath.ToString(), out var fileStat) == 0)
            return (fileStat.st_mode & FilePermissions.S_IXUSR) != 0;
        return false;
    }
    
    private static Suggestion[] FindExecutablesInPathEnvironment()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        return pathEnv == null 
            ? [] 
            : pathEnv.Split(":")
                .Select(UnescapeFileName)
                .ToNPaths()
                .Where(path => path.DirectoryExists())
                .SelectMany(FindExecutablesInPath)
                .ToArray();
    }

    [GeneratedRegex(@"(?<!\\) ")]
    private static partial Regex CommandLineWordsRegex();
}