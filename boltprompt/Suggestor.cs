using System.Collections;
using System.ComponentModel;
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
    private static readonly Dictionary<string, string> FileDescriptionCache = new();

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
        public CommandInfo.ArgumentType ArgumentType;
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

    class ArgumentParsingState
    {
        public ArgumentParsingState(CommandInfo.ArgumentGroup[] groups)
        {
            Groups = groups;
            InitEligibleArguments();
        }
        
        public CommandInfo.ArgumentGroup[] Groups = [];
        public List<(CommandInfo.Argument argument, int groupIndex)> EligibleArguments = new();
        public int MinGroupIndex = 0;
        public int MaxGroupIndex = 0;

        public void InitEligibleArguments(int startIndex = 0)
        {
            MinGroupIndex = startIndex;
            var groupIndex = MinGroupIndex;
            EligibleArguments.Clear();
            while (Groups.Length > groupIndex)
            {
                var group = Groups[groupIndex];
                EligibleArguments.AddRange(group.Arguments.Select(a => (a, groupIndex)));
                if (!group.Optional)
                    break;
                groupIndex++;
                if (groupIndex > MaxGroupIndex)
                    MaxGroupIndex = groupIndex;
            }
        }
    }

    static CommandInfo.Argument? ParseArgument(string arg, Stack<ArgumentParsingState> parsingState)
    {
        if (parsingState.Count == 0)
            return null;
        var currentState = parsingState.Peek();
        foreach (var argToMatch in currentState.EligibleArguments)
        {
            if (CanMatchArgument(arg, argToMatch.argument))
            {
                currentState.MinGroupIndex = argToMatch.groupIndex;
                if (!currentState.Groups[argToMatch.groupIndex].Optional)
                    currentState.InitEligibleArguments(argToMatch.groupIndex + 1);
                else
                    currentState.EligibleArguments = currentState.EligibleArguments.Where(a => a.groupIndex >= currentState.MinGroupIndex && (a != argToMatch || a.argument.Repeat)).ToList();
                if (argToMatch.argument.Arguments != null)
                    parsingState.Push(new (argToMatch.argument.Arguments));

                return argToMatch.argument;
            }
        }

        if (currentState.MaxGroupIndex == currentState.Groups.Length)
        {
            parsingState.Pop();
            var result = ParseArgument(arg, parsingState);
            if (result != null)
                return result;
            parsingState.Push(currentState);
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
                return argToMatch.AllNames.Any(a => $"-{a}" == arg); // Todo Flag groups
            case CommandInfo.ArgumentType.FileSystemEntry:
            case CommandInfo.ArgumentType.Directory:
            case CommandInfo.ArgumentType.File:
            case CommandInfo.ArgumentType.Command:
            case CommandInfo.ArgumentType.CommandName:
            case CommandInfo.ArgumentType.ProcessId:
            case CommandInfo.ArgumentType.ProcessName:
            case CommandInfo.ArgumentType.String:
                return true;
            case CommandInfo.ArgumentType.CustomArgument:
                return CustomArguments.Get(argToMatch.Name).Any(a => a.Text == arg);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static IEnumerable<CommandLinePart> ParseCommandLine(string commandline)
    {
        var pos = 0;
        var lastPartType = CommandLinePart.PartType.Whitespace;
        var commandinfo = CommandInfo.DefaultCommand;
        var parsingState = new Stack<ArgumentParsingState>();
        while (pos < commandline.Length)
        {
            var nextPos = pos;
            while (nextPos < commandline.Length && char.IsWhiteSpace(commandline[nextPos])) nextPos++;
            
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

            while (nextPos < commandline.Length && !char.IsWhiteSpace(commandline[nextPos]) && !_shellOperators.Contains(commandline[nextPos])) nextPos++;

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
                lastPartType = part.Type;
                if (part.Type == CommandLinePart.PartType.Command)
                {
                    commandinfo = KnownCommands.GetCommand(part.Text.Split('/').Last(), false);
                    parsingState = new ();
                    if (commandinfo?.Arguments != null)
                        parsingState.Push(new (commandinfo.Arguments));
                }

                if (part.Type == CommandLinePart.PartType.Argument)
                {
                    var parsedArgument = ParseArgument(part.Text, parsingState);
                    if (parsedArgument != null)
                        part.ArgumentType = parsedArgument.Type;
                }

                yield return part;
                pos = nextPos;
            }
        }
    }
    
    class HistoryComparer : IComparer<Suggestion>
    {
        private readonly string[] _historyFilteredByCommandline;
        
        public HistoryComparer(string commandline)
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
            return string.Compare(y.Text, x.Text, StringComparison.Ordinal);
        }
    }
    
    static Suggestion[] SortSuggestionsByHistory(string commandline, IEnumerable<Suggestion> suggestions)
    {
        return string.IsNullOrWhiteSpace(commandline) ? suggestions.ToArray() : suggestions.OrderDescending(new HistoryComparer(commandline)).ToArray();
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
            return SuggestByAI(commandline);
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

    private static Suggestion[] SuggestByAI(string commandline)
    {
        if (!ChatGptClient.IsAvailable)
            return [];

        return AISuggestor.Suggest(commandline[1..]);
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
    public static string UnescapeFileName(string fileName) => fileName.Replace(@"\ ", " ").Replace(@"\\", @"\").Replace("~", NPath.HomeDirectory.ToString());
    private static string NPathToSuggestionText(string prefix, NPath parent, NPath path)
        => $"{prefix}{EscapeFileName(path.RelativeTo(parent).ToString())}{(path.DirectoryExists() ? '/' : ' ')}";

    private static Suggestion[] SuggestFileSystemEntries(string commandline, CommandInfo.ArgumentType type)
    {
        var split = SplitCommandIntoWords(commandline);
        var currentArg = split.Last();
        var dir = NPath.CurrentDirectory;
        var prefix = "";
        if (currentArg.Contains('/'))
        {
            prefix = currentArg[..(currentArg.LastIndexOf('/') + 1)];
            dir = prefix.Replace("\\ ", " ");
        }
        
        if (dir.ToString().StartsWith('~'))
            dir = NPath.HomeDirectory.Combine(dir.RelativeTo("~"));

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
            .Where(fs => type != CommandInfo.ArgumentType.CommandName || fs.DirectoryExists() || (IsExecutable(fs) && !string.IsNullOrEmpty(prefix)))
            .OrderBy(fs => fs.FileName)
            .Select(Suggestion (fs) => new FileSystemSuggestion(NPathToSuggestionText(prefix, dir, fs)) { Icon = fs.DirectoryExists()?"📁" : "📄"})
            .ToArray();
    }
    
    private static IEnumerable<Suggestion> SuggestParameters(string commandline)
    {
        var commandWords = SplitCommandIntoWords(commandline);
        var command = commandWords.FirstOrDefault("");
        var commandParams = commandWords.Skip(1).ToArray();
        var lastParam = commandParams.LastOrDefault("");

        var executableExists = GetExecutableCommandInfo(command) != null;
        var ci = KnownCommands.GetCommand(command.Split('/').Last(), executableExists) ?? CommandInfo.DefaultCommand;
        if (ci.Arguments == null)
            yield break;

        var lastParamIsFinished = commandline.LastOrDefault(' ') == ' ';
        var arguments = GetEligibleArguments(ci.Arguments, commandParams, lastParamIsFinished, out var paramPrefix, out var curArg);

        if (curArg != null && !lastParamIsFinished)
            yield return new (paramPrefix) { Description = curArg.Description };

        foreach (var arg in arguments)
        {
            switch (arg.Type)
            {
                case CommandInfo.ArgumentType.Flag:
                    if (paramPrefix == "" && (lastParam.Length == 0 || lastParam.StartsWith('-')))
                    {
                        foreach (var v in arg.AllNames)
                            yield return new("-" + v) { Description = arg.Description };
                    }
                    else if (paramPrefix.StartsWith('-'))
                    {
                        foreach (var v in arg.AllNames)
                            yield return new(paramPrefix + v) { Description = arg.Description };
                    }
                    break;
                case CommandInfo.ArgumentType.Keyword:
                    foreach (var v in arg.AllNames)
                    {
                        if (v.StartsWith(lastParam))
                            yield return new(paramPrefix + v) { Description = arg.Description };
                    }

                    break;
                case CommandInfo.ArgumentType.CommandName:
                    foreach (var s in _executablesInPathEnvironment.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;
                    break;
                case CommandInfo.ArgumentType.Command:
                    foreach (var s in SuggestionsForPrompt(string.Join(' ',commandParams)))
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
                    foreach (var s in SuggestFileSystemEntries(commandline, arg.Type))
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
                    foreach (var s in CustomArguments.Get(arg.Name))
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

    private static List<CommandInfo.Argument> GetEligibleArguments(CommandInfo.ArgumentGroup[] ciArguments, string[] commandParams, bool lastParamIsFinished, out string paramPrefix, out CommandInfo.Argument? curArg)
    {
        var arguments = new List<CommandInfo.Argument>();
        
        paramPrefix = "";
        curArg = null;

        var commandQueue = new Queue<string>(commandParams);
        if (commandQueue.Count == 0)
            return arguments;
        
        var lastParam = commandQueue.Dequeue();
        foreach (var argGroup in ciArguments)
        {
            var argGroupArguments = new List<CommandInfo.Argument>(argGroup.Arguments);
            var keepParsing = true;
            var gotMatch = false;
            while (keepParsing)
            {
                keepParsing = false;

                foreach (var arg in argGroupArguments)
                {
                    switch (arg.Type)
                    {
                        case CommandInfo.ArgumentType.Keyword:
                        case CommandInfo.ArgumentType.Flag:
                            foreach (var v in arg.AllNames)
                            {
                                var name = v;
                                if (paramPrefix == "" && arg.Type == CommandInfo.ArgumentType.Flag)
                                    name = $"-{v}";
                                if (commandQueue.Count != 0 ? lastParam != name : !lastParam.StartsWith(name)) continue;
                                gotMatch = true;
                                if (arg.Arguments != null)
                                {
                                    argGroupArguments = arg.Arguments.SelectMany(a => a.Arguments).Concat(arg.DontAllowMultiple ? [] : argGroupArguments.Where(a => a != arg || arg.Repeat)).ToList();
                                    paramPrefix += lastParam[..name.Length];
                                    lastParam = lastParam[name.Length..];
                                    curArg = arg;
                                    keepParsing = true;
                                }
                                else if (arg.Type == CommandInfo.ArgumentType.Flag)
                                {
                                    argGroupArguments = arg.DontAllowMultiple ? [] : argGroupArguments.Where(a =>
                                        a.Type == CommandInfo.ArgumentType.Flag && (a != arg || arg.Repeat)).ToList();
                                    paramPrefix += lastParam[..name.Length];
                                    lastParam = lastParam[name.Length..];
                                    curArg = arg;
                                    keepParsing = true;
                                }
                                else
                                    argGroupArguments = arg.DontAllowMultiple ? [] : argGroupArguments.Where(a => a != arg || arg.Repeat).ToList();
                            }
                            break;
                        case CommandInfo.ArgumentType.FileSystemEntry:
                        case CommandInfo.ArgumentType.Directory:
                        case CommandInfo.ArgumentType.File:
                        case CommandInfo.ArgumentType.CommandName:
                        case CommandInfo.ArgumentType.ProcessId:
                        case CommandInfo.ArgumentType.String: 
                            if (!gotMatch && lastParam.Length != 0 && commandQueue.Count > 0)
                            {
                                argGroupArguments = [];
                                gotMatch = true;
                                curArg = arg;
                                paramPrefix = lastParam;
                                lastParam = "";
                            }
                            break;
                        
                        case CommandInfo.ArgumentType.Command:
                            argGroupArguments = [arg];
                            break;
                    }
                    if (lastParam.Length != 0 || commandQueue.Count == 0) continue;
                        
                    lastParam = commandQueue.Dequeue();
                    paramPrefix = "";
                    curArg = null;
                    
                }
            }

            if (gotMatch)
            {
                arguments = argGroupArguments;
                if (lastParam.Length == 0 && !lastParamIsFinished)
                    break;
            }
            else
            {
                arguments.AddRange(argGroup.Arguments);
                if (!argGroup.Optional)
                    break;
            }
        }

        return arguments;
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