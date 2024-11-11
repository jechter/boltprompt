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
    private static Suggestion[] _executablesInPathEnvironment = [];
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

    class SuggestionSorter : IComparer<Suggestion>
    {
        private readonly string[] _historyFilteredByCommandline;
        private string lastCommandLineWord;
        
        public SuggestionSorter(string commandline)
        {
            if (string.IsNullOrWhiteSpace(commandline))
            {
                _historyFilteredByCommandline = History.Commands.Select(s => s.Commandline.Trim()).ToArray();
                lastCommandLineWord = "";
            }
            else
            {
                var parsedCommandLine = CommandLineParser.ParseCommandLine(commandline).ToArray();
                var lastCommandLinePart = parsedCommandLine.Last();
                lastCommandLineWord = lastCommandLinePart.Type == CommandLineParser.CommandLinePart.PartType.Argument
                    ? lastCommandLinePart.Text
                    : "";
                var commandLineWordPathComponents = lastCommandLinePart.Text.Split('/');
                _historyFilteredByCommandline =
                    History.Commands.Where(s => s.Commandline.StartsWith(commandline))
                        .Select(FilterHistoryEntryByCommandLine)
                        .Where(s => s != null)
                        .Cast<string>()
                        .ToArray();

                string? FilterHistoryEntryByCommandLine(History.Command historyEntry)
                {
                    var parsedHistoryCommandLine = historyEntry.ParsedCommandLine;
                    var index = lastCommandLinePart.Type == CommandLineParser.CommandLinePart.PartType.Whitespace
                        ? parsedCommandLine.Length
                        : parsedCommandLine.Length - 1;
                    if (index >= parsedCommandLine.Length)
                        return null;
                    var part = parsedHistoryCommandLine[index];
                    if (part is
                        not
                        {
                            Type: CommandLineParser.CommandLinePart.PartType.Argument,
                            Argument.Type: CommandInfo.ArgumentType.FileSystemEntry or CommandInfo.ArgumentType.File
                            or CommandInfo.ArgumentType.Directory
                        }) return part.Text;
                    var historyEntryPathComponents = part.Text.Split('/');
                    var filteredHistoryEntryPathComponents =
                        historyEntryPathComponents.Take(commandLineWordPathComponents.Length);
                    if (commandLineWordPathComponents.Length < historyEntryPathComponents.Length)
                        return string.Join('/', filteredHistoryEntryPathComponents) + "/";
                    return part.Text;
                }
            }
        }
        
        public int Compare(Suggestion? x, Suggestion? y)
        { 
            if (ReferenceEquals(x, y)) return 0;
            if (y is null) return 1;
            if (x is null) return -1;

            var yIsPrefixed = y.Text.StartsWith(lastCommandLineWord);
            var xIsPrefixed = x.Text.StartsWith(lastCommandLineWord);
            if (xIsPrefixed != yIsPrefixed)
                return xIsPrefixed ? 1 : -1;
            
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

    private static Suggestion[] SortSuggestions(string commandline, IEnumerable<Suggestion> suggestions)
    {
        var result = string.IsNullOrWhiteSpace(commandline) ? suggestions.ToList() : suggestions.OrderDescending(new SuggestionSorter(commandline)).ToList();
        for (var i = 1; i < result.Count; i++)
        {
            // TODO: maybe we have to check which one we remove
            // (to avoid fake matches for keywords as string arguments suggested from history) 
            if (result[i-1].Text == result[i].Text)
                result.RemoveAt(i--);
        }

        // empty suggestions don't make sense, unless it's the only one (to have a description of what the next parameter is for)
        if (result.Count > 1)
            result = result.Where(r => r.Text != "").ToList();
        
        return result.ToArray();
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
        if (commandline.StartsWith(Configuration.Instance.AIPromptPrefix))
        {
            var aiPrompt = commandline[Configuration.Instance.AIPromptPrefix.Length..];
            return AISuggestor.Suggest(aiPrompt)
                .Concat(
                    History.Commands.Where(h => h.AIPrompt != null)
                        .Select(h => h.AIPrompt!)
                        .Concat(AISuggestor.DefaultPromptSuggestions)
                        .Where(prompt => prompt.StartsWith(aiPrompt) && prompt.Length > aiPrompt.Length)
                        .Select(h => $"{Configuration.Instance.AIPromptPrefix}{h}")
                        .Reverse()
                        .Distinct()
                        .Select(h => new Suggestion(h))
                )
                .ToArray();
        }

        var parsed = CommandLineParser.ParseCommandLine(commandline).ToArray();
        var currentPart = parsed.LastOrDefault(new CommandLineParser.CommandLinePart("") { Type = CommandLineParser.CommandLinePart.PartType.Command });
        var suggestions = currentPart.Type == CommandLineParser.CommandLinePart.PartType.Command
            ? SuggestCommand(currentPart.Text)
            : SuggestParameters(commandline).ToArray();
        if (currentPart.Text.StartsWith('$'))
            suggestions = SuggestEnvironmentVariables(currentPart.Text).Concat(suggestions).ToArray();
        return SortSuggestions(commandline, suggestions);
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
        var parsingState = new List<CommandLineParser.ArgumentParsingState>();
        var parts = CommandLineParser.ParseCommandLine(commandline, parsingState).ToArray();
        var lastParam = "";
        
        if (parts[^1].Type != CommandLineParser.CommandLinePart.PartType.Whitespace)
        {
            var commandLineToParse = string.Concat(parts[..^1].Select(p => p.Text));
            lastParam = parts[^1].Text;
            parts = parts[..^1];
            parsingState = [];
            _ = CommandLineParser.ParseCommandLine(commandLineToParse, parsingState).Count(); // We do Count here to force running the iterator to the end
        }
        
        var arguments = CommandLineParser.GetEligibleArgumentsForState(parsingState).Select(a => a.argument).ToArray();
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
                            yield return FlagSuggestion("-" + v);
                    }
                    else if (lastParam.StartsWith('-'))
                    {
                        if (lastParam[1..].Any(c => !allFlags.Contains(c)))
                            break;
                        
                        foreach (var v in arg.AllNames)
                            if (lastParam[^1] == v[0])
                                yield return FlagSuggestion(lastParam);
                        foreach (var v in arg.AllNames)
                            if (!lastParam.Contains(v[0]) || arg.Repeat)
                                yield return FlagSuggestion(lastParam + v);
                    }
                    break;

                    Suggestion FlagSuggestion(string text) => new(text)
                    {
                        Description = arg.Description,
                        Icon = "⚐"
                    };
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
                        if (s.Text.Contains(lastParam) || (s.Description?.Contains(lastParam) ?? false))
                        {
                            yield return s;
                            hasMatch = true;
                        }
                    }
                    break;
                case CommandInfo.ArgumentType.String:
                    yield return new (lastParam) { Description = string.IsNullOrEmpty(arg.Description) ? arg.Name : arg.Description };
                    foreach (var c in History.Commands
                                 .Where(cmd => cmd.Commandline.StartsWith(commandline) && cmd.ParsedCommandLine.Length > parts.Length)
                                 .Select(cmd => cmd.ParsedCommandLine[parts.Length].Text)
                             )
                        yield return new (c) { Description = string.IsNullOrEmpty(arg.Description) ? arg.Name : arg.Description };
                    
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