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
            Argument,
            Operator,
            Whitespace,
        }

        public PartType Type;
        public CommandInfo.Argument? Argument;
    }
    
    private static Suggestion[] _executablesInPathEnvironment = [];
    private static readonly char[] _shellOperators = ['>', '<', '|', '&', ';']; 
    
    public static Suggestion[] ExecutablesInPathEnvironment => _executablesInPathEnvironment;
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
        public ArgumentParsingState(CommandInfo.ArgumentGroup[] groups)
        {
            Groups = groups;
            LoadEligibleArguments();
        }
        
        public CommandInfo.ArgumentGroup[] Groups = [];
        public List<(CommandInfo.Argument argument, int groupIndex)> EligibleArguments = new();
        public int MinGroupIndex = 0;
        public int MaxGroupIndex = 0;

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
    
    static CommandInfo.Argument? ParseArgument(string arg, List<ArgumentParsingState> parsingState)
    {
        foreach (var (argument, groupIndex, depth) in GetEligibleArgumentsForState(parsingState))
        {
            if (!CanMatchArgument(arg, argument)) continue;
            
            parsingState.RemoveRange(parsingState.Count + 1 - depth, depth - 1);
            var currentState = parsingState.Last();
            currentState.MinGroupIndex = groupIndex;
            if (!currentState.Groups[groupIndex].Optional)
                currentState.LoadEligibleArguments(groupIndex + 1);
            if (currentState.Groups[groupIndex].DontAllowMultiple)
                currentState.MinGroupIndex = groupIndex + 1;
            currentState.EligibleArguments = currentState.EligibleArguments.Where(a => a.groupIndex >= currentState.MinGroupIndex && (a.argument != argument || a.argument.Repeat)).ToList();
                
            if (argument.Arguments != null)
                parsingState.Add(new (argument.Arguments));
                
            return argument;
        }
       
        return null;
    }

    private static bool CanMatchArgument(string arg, CommandInfo.Argument argToMatch)
    {
        switch (argToMatch.Type)
        {
            case CommandInfo.ArgumentType.Keyword:
                return argToMatch.AllNames.Any(a => a == arg);
            case CommandInfo.ArgumentType.Flag:
                return argToMatch.AllNames.Any(a => arg.StartsWith($"-{a}"));
            case CommandInfo.ArgumentType.File:
                return !(argToMatch.Extensions?.Length > 0) 
                       || arg.ToNPath().HasExtension(argToMatch.Extensions);
            case CommandInfo.ArgumentType.FileSystemEntry:
            case CommandInfo.ArgumentType.Directory:
            case CommandInfo.ArgumentType.Command:
            case CommandInfo.ArgumentType.CommandName:
            case CommandInfo.ArgumentType.ProcessId:
            case CommandInfo.ArgumentType.ProcessName:
            case CommandInfo.ArgumentType.String:
                return true;
            case CommandInfo.ArgumentType.CustomArgument:
                return argToMatch.CustomCommand != null && CustomArguments.Get(argToMatch).Any(a => a.Text == arg);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

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
            
            if (_shellOperators.Contains(commandline[pos]))
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
                while (nextPos < commandline.Length && !IsWhiteSpace(nextPos) && !_shellOperators.Contains(commandline[nextPos])) nextPos++;

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
                        _ => throw new ArgumentOutOfRangeException()
                    }
                };

                // Variable assignment
                if (part.Type == CommandLinePart.PartType.Command && part.Text.Contains('='))
                    part.Type = CommandLinePart.PartType.Operator;
                
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
            NPath commandPath = part.Text;
            var createCommandInfo = commandPath.FileExists() || _executablesInPathEnvironment.Any(s => commandPath.FileName == s.Text.Trim());
            var commandInfo = KnownCommands.GetCommand(commandPath.FileName, createCommandInfo);
            parsingState.Clear();

            if (commandInfo?.Arguments != null)
                parsingState.Add(new (commandInfo.Arguments));
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

        var commandLineCommands = commandline.Split(_shellOperators);
        var currentCommand = commandLineCommands.Last().TrimStart();
        var commandLineArguments = SplitCommandIntoWords(currentCommand);
        var command = commandLineArguments.FirstOrDefault("");
        var currentWord = commandLineArguments.LastOrDefault();
        var suggestions = commandLineArguments.Length <= 1
            ? SuggestCommand(command)
            : SuggestParameters(currentCommand).ToArray();
        if (currentWord?.StartsWith('$') ?? false)
            suggestions = SuggestEnvironmentVariables(currentWord).Concat(suggestions).ToArray();
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

    static string EscapeFileName(string fileName) => fileName.Replace(@"\", @"\\").Replace(" ", @"\ ");

    private static Dictionary<string, string> userDirCache = new();
    
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
           if (!userDirCache.TryGetValue(userNameEscape, out var userDir))
           {
               var result = Cli.Wrap("sh").WithArguments($"-c \"echo {userNameEscape}\"").ExecuteBufferedAsync().GetAwaiter().GetResult();
               userDir = result.StandardOutput.Trim('\n', ' ');
               userDirCache[userNameEscape] = userDir;
           }
           unescaped = unescaped.Replace(userNameEscape, userDir);
       }
       return unescaped;
    } 
    
    private static string NPathToSuggestionText(string prefix, NPath parent, NPath path)
        => $"{prefix}{EscapeFileName(path.RelativeTo(parent).ToString())}{(path.DirectoryExists() ? '/' : ' ')}";
    
    private static Suggestion[] SuggestFileSystemEntries(string commandline, CommandInfo.ArgumentType type, string[]? extensions = null)
    {
        var split = SplitCommandIntoWords(commandline);
        var currentArg = split.Last();
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
        
        var arguments = GetEligibleArgumentsForState(parsingState).Select(a => a.argument);

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
                        if (v.StartsWith(lastParam))
                            yield return new(v) { Description = arg.Description };
                    }

                    break;
                case CommandInfo.ArgumentType.CommandName:
                case CommandInfo.ArgumentType.Command:
                    foreach (var s in _executablesInPathEnvironment.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;
                    break;
                case CommandInfo.ArgumentType.ProcessId:
                    foreach (var p in GetProcesses())
                    {
                        var pidString = p.pid.ToString();
                        if (pidString.StartsWith(lastParam))
                            yield return new (pidString) { Description = p.name };
                    }

                    break;
                case CommandInfo.ArgumentType.ProcessName:
                    foreach (var p in GetProcesses().Select(p => p.name.ToNPath().FileName).Where(p => p.StartsWith(lastParam)).Distinct())
                        yield return new(p);

                    break;
                case CommandInfo.ArgumentType.FileSystemEntry:
                case CommandInfo.ArgumentType.Directory:
                case CommandInfo.ArgumentType.File:
                    bool hasMatch = false;
                    foreach (var s in SuggestFileSystemEntries(commandline, arg.Type, arg.Extensions))
                    {
                        if (s.Text.StartsWith(lastParam))
                        {
                            yield return s with { Description = arg.Description };
                            hasMatch = true;
                        }
                    }
                    // if we have no matching files, return a match for whatever was typed to allow creating new paths.
                    if (!hasMatch)
                        yield return new (lastParam) { Description = string.IsNullOrEmpty(arg.Description) ? arg.Name : arg.Description };
                    break;
                case CommandInfo.ArgumentType.CustomArgument:
                    foreach (var s in CustomArguments.Get(arg))
                        if (s.Text.StartsWith(lastParam))
                            yield return s;
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

    private static IEnumerable<(int pid, string name)> GetProcesses()
    {
        var commandResult = Cli.Wrap("ps")
            .WithArguments("-Ao pid,comm")
            .ExecuteBufferedAsync()
            .GetAwaiter()
            .GetResult();
        var lines = commandResult.StandardOutput.Split('\n');
        foreach (var line in lines.Skip(1).Select(l => l.Trim()))
        {
            var split = line.Split(' ');
            if (split.Length == 2)
                yield return (int.Parse(split[0]), split[1]);
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