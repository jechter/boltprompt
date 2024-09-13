using Mono.Unix.Native;
using NiceIO;

namespace Shelper;

public record Suggestion(string Text)
{
    public string? Icon;
    public string? Description;
}

public class Suggestor
{
    private readonly Suggestion[] _executablesInPathEnvironment;
    private readonly char[] _shellOperators = ['>', '<', '|', '&', ';']; 
    
    Suggestion? GetExecutableCommandInfo(string command) =>
        _executablesInPathEnvironment.FirstOrDefault(exe => exe.Text.Trim() == Path.GetFileName(command));
    
    public Suggestor()
    {
        _executablesInPathEnvironment = FindExecutablesInPathEnvironment();
        KnownCommands.CommandInfoLoaded += ci =>
        {
            var sug = GetExecutableCommandInfo(ci.Name);
            if (sug != null)
                sug.Description = ci.Description;
        };
    }

    Suggestion[] SortSuggestionsByHistory(string commandline, IEnumerable<Suggestion> suggestions)
    {
        var commandLineWords = commandline.Split(' ');
        var lastCommandLineWord = commandLineWords.Last();
        var commandLineWordPathComponents = lastCommandLineWord.Split('/');

        var historyFilteredByCommandline = 
            History.Commands.Where(s => s.StartsWith(commandline)).Select(FilterHistoryEntryByCommandLine).ToArray();
        
        return suggestions.OrderByDescending(sug => Array.LastIndexOf(historyFilteredByCommandline, sug.Text.Trim())).ToArray();

        string FilterHistoryEntryByCommandLine(string historyEntry)
        {
            var historyEntryWords = historyEntry.Split(' ');
            var historyEntryCommandLineWord = historyEntryWords[commandLineWords.Length - 1];
            var historyEntryPathComponents = historyEntryCommandLineWord.Split('/');
            var filteredHistoryEntryPathComponents = historyEntryPathComponents.Take(commandLineWordPathComponents.Length);
            if (commandLineWordPathComponents.Length < historyEntryPathComponents.Length)
                return string.Join('/', filteredHistoryEntryPathComponents) + "/";
            return historyEntryCommandLineWord;
        }
    }

    public Suggestion[] SuggestionsForPrompt(string commandline)
    {
        var commandLineCommands = commandline.Split(_shellOperators);
        var currentCommand = commandLineCommands.Last().TrimStart();
        var commandLineArguments = currentCommand.Split(' ');
        var command = commandLineArguments[0];
        return SortSuggestionsByHistory(commandline, commandLineArguments.Length == 1 ? SuggestCommand(command) : SuggestParameters(command, commandline).ToArray());
    }

    private static Suggestion[] SuggestFileSystemEntries(string commandline, CommandInfo.ArgumentType type)
    {
        var split = commandline.Split(' ');
        var currentArg = split.Last();
        var dir = NPath.CurrentDirectory;
        var prefix = "";
        if (currentArg.Contains('/'))
        {
            prefix = currentArg[..(currentArg.LastIndexOf('/') + 1)];
            dir = prefix;
        }
        
        if (dir.ToString().StartsWith('~'))
            dir = NPath.HomeDirectory.Combine(dir.RelativeTo("~"));

        if (!dir.DirectoryExists())
            return [];
        
        return (prefix == "" ? new []{dir}:[]).Concat(type == CommandInfo.ArgumentType.Directory ? dir.Directories() : dir.Contents())
            .Where(fs => type != CommandInfo.ArgumentType.Command || fs.DirectoryExists() || (IsExecutable(fs) && !string.IsNullOrEmpty(prefix)))
            .OrderBy(fs => fs.FileName)
            .Select(fs => new Suggestion($"{prefix}{fs.RelativeTo(dir)}{(fs.DirectoryExists()?'/':' ')}") { Icon = fs.DirectoryExists()?"📁" : "📄"})
            .ToArray();
    }
    
    private IEnumerable<Suggestion> SuggestParameters(string command, string commandline)
    {
        var executableExists = GetExecutableCommandInfo(command) != null;
        var ci = KnownCommands.GetCommand(command.Split('/').Last(), executableExists) ?? CommandInfo.DefaultCommand;
        if (ci.Arguments == null)
            yield break;
        var lastParam = commandline.Split(' ').Last();

        var arguments = GetEligibleArguments(ci.Arguments, out var paramPrefix, out var curArg, ref lastParam);

        if (curArg != null)
            yield return new (paramPrefix) { Description = curArg.Description };

        foreach (var arg in arguments)
        {
            switch (arg.Type)
            {
                case CommandInfo.ArgumentType.Flag:
                {
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
                }
                case CommandInfo.ArgumentType.Keyword:
                {
                    foreach (var v in arg.AllNames)
                    {
                        if (v.StartsWith(lastParam))
                            yield return new(paramPrefix + v) { Description = arg.Description };
                    }

                    break;
                }
                case CommandInfo.ArgumentType.Command:
                {
                    foreach (var s in _executablesInPathEnvironment.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;

                    break;
                }
                case CommandInfo.ArgumentType.FileSystemEntry:
                case CommandInfo.ArgumentType.Directory:
                case CommandInfo.ArgumentType.File:
                {
                    foreach (var s in SuggestFileSystemEntries(commandline, arg.Type))
                        if (s.Text.StartsWith(lastParam))
                            yield return s with { Description = arg.Description };
                    break;
                }
                case CommandInfo.ArgumentType.String:
                    // We have no suggestions for generic strings
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static List<CommandInfo.Argument> GetEligibleArguments(CommandInfo.Argument[][] ciArguments, out string paramPrefix, out CommandInfo.Argument? curArg,
        ref string lastParam)
    {
        var arguments = new List<CommandInfo.Argument>();
        paramPrefix = "";
        curArg = null;

        foreach (var argGroup in ciArguments)
        {
            var argGroupArguments = new List<CommandInfo.Argument>(argGroup);
            var keepParsing = true;
            var gotMatch = false;
            while (keepParsing)
            {
                keepParsing = false;

                foreach (var arg in argGroup)
                {
                    if (arg.Type != CommandInfo.ArgumentType.Keyword &&
                        arg.Type != CommandInfo.ArgumentType.Flag) continue;
                    foreach (var v in arg.AllNames)
                    {
                        var name = v;
                        if (paramPrefix == "")
                            name = $"-{v}";
                        if (!lastParam.StartsWith(name)) continue;
                        gotMatch = true;
                        if (arg.Arguments != null)
                        {
                            argGroupArguments = arg.Arguments.SelectMany(a => a).ToList();
                            paramPrefix += lastParam[..name.Length];
                            lastParam = lastParam[name.Length..];
                            curArg = arg;
                            keepParsing = true;
                        }
                        else if (arg.Type == CommandInfo.ArgumentType.Flag)
                        {
                            argGroupArguments = argGroupArguments.Where(a =>
                                a.Type == CommandInfo.ArgumentType.Flag && (a != arg || arg.Repeat)).ToList();
                            paramPrefix += lastParam[..name.Length];
                            lastParam = lastParam[name.Length..];
                            curArg = arg;
                            keepParsing = true;
                        }
                    }
                }
            }

            if (gotMatch)
                arguments = argGroupArguments;
            else
                arguments.AddRange(argGroup);
        }

        return arguments;
    }
    
    private Suggestion[] SuggestCommand(string commandline)
    {
        if (string.IsNullOrEmpty(commandline))
            return History.Commands.Select(h => new Suggestion(h)).ToArray();
        return _executablesInPathEnvironment
            .Concat(
                SuggestFileSystemEntries(commandline, CommandInfo.ArgumentType.Command)
                    .Select(sug => sug with { Description = KnownCommands.GetCommand(sug.Text.Split('/').Last().Trim(), false)?.Description ?? "" })
                )
            .Concat(History.Commands.Select(h => new Suggestion(h)))
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
                .ToNPaths()
                .Where(path => path.DirectoryExists())
                .SelectMany(FindExecutablesInPath)
                .ToArray();
    }
}