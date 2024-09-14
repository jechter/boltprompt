using System.Collections;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using Mono.Unix.Native;
using NiceIO;

namespace Shelper;

public record Suggestion(string Text)
{
    public string? Icon;
    public string? Description;
}

public partial class Suggestor
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

    public static string[] SplitCommandIntoWords(string currentCommand) => CommandLineWordsRegex().Split(currentCommand);
    public Suggestion[] SuggestionsForPrompt(string commandline)
    {
        var commandLineCommands = commandline.Split(_shellOperators);
        var currentCommand = commandLineCommands.Last().TrimStart();
        var commandLineArguments = SplitCommandIntoWords(currentCommand);
        var command = commandLineArguments.First();
        var currentWord = commandLineArguments.Last();
        var suggestions = commandLineArguments.Length == 1
            ? SuggestCommand(command)
            : SuggestParameters(currentCommand).ToArray();
        if (currentWord.StartsWith("$"))
            suggestions = SuggestEnvironmentVariables(currentWord).Concat(suggestions).ToArray();
        return SortSuggestionsByHistory(commandline, suggestions);
    }

    private static IEnumerable<Suggestion> SuggestEnvironmentVariables(string currentWord)
    {
        var currentWordNoPrefix = currentWord[1..];
        var env = Environment.GetEnvironmentVariables();
        foreach (DictionaryEntry e in env)
        {
            var key = e.Key.ToString();
            if (key?.StartsWith(currentWordNoPrefix) ?? false)
                yield return new($"${e.Key}") { Description = e.Value?.ToString() };
        }
    }

    private static string NPathToSuggestionText(string prefix, NPath parent, NPath path)
        => $"{prefix}{path.RelativeTo(parent).ToString().Replace(@"\", @"\\").Replace(" ", @"\ ")}{(path.DirectoryExists() ? '/' : ' ')}";

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
        
        return (prefix == "" ? new []{dir}:[]).Concat(type == CommandInfo.ArgumentType.Directory ? dir.Directories() : dir.Contents())
            .Where(fs => type != CommandInfo.ArgumentType.CommandName || fs.DirectoryExists() || (IsExecutable(fs) && !string.IsNullOrEmpty(prefix)))
            .OrderBy(fs => fs.FileName)
            .Select(fs => new Suggestion(NPathToSuggestionText(prefix, dir, fs)) { Icon = fs.DirectoryExists()?"📁" : "📄"})
            .ToArray();
    }
    
    private IEnumerable<Suggestion> SuggestParameters(string commandline)
    {
        var commandWords = SplitCommandIntoWords(commandline);
        var command = commandWords.First();
        var commandParams = commandWords.Skip(1).ToArray();
        var lastParam = commandParams.Last();

        var executableExists = GetExecutableCommandInfo(command) != null;
        var ci = KnownCommands.GetCommand(command.Split('/').Last(), executableExists) ?? CommandInfo.DefaultCommand;
        if (ci.Arguments == null)
            yield break;

        var arguments = GetEligibleArguments(ci.Arguments, commandParams, out var paramPrefix, out var curArg);

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
                case CommandInfo.ArgumentType.CommandName:
                case CommandInfo.ArgumentType.Command:
                {
                    foreach (var s in _executablesInPathEnvironment.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;

                    break;
                }
                case CommandInfo.ArgumentType.ProcessId:
                {
                    foreach (var p in GetProcesses())
                    {
                        var pidString = p.pid.ToString();
                        if (pidString.StartsWith(lastParam))
                            yield return new (pidString) { Description = p.name };
                    }

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

    private IEnumerable<(int pid, string name)> GetProcesses()
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

    private static List<CommandInfo.Argument> GetEligibleArguments(CommandInfo.Argument[][] ciArguments, string[] commandParams, out string paramPrefix, out CommandInfo.Argument? curArg)
    {
        var arguments = new List<CommandInfo.Argument>();

        paramPrefix = "";
        curArg = null;

        var commandQueue = new Queue<string>(commandParams);
        var lastParam = commandQueue.Dequeue();
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
                        if (paramPrefix == "" && arg.Type == CommandInfo.ArgumentType.Flag)
                            name = $"-{v}";
                        if (commandQueue.Count != 0 ? lastParam != name : !lastParam.StartsWith(name)) continue;
                        gotMatch = true;
                        if (arg.Arguments != null)
                        {
                            argGroupArguments = argGroupArguments.Where(a => a != arg || arg.Repeat).ToList();
                            argGroupArguments.AddRange(arg.Arguments.SelectMany(a => a));
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
                        else
                            argGroupArguments = argGroupArguments.Where(a => a != arg || arg.Repeat).ToList();

                        if (lastParam.Length == 0 && commandQueue.Count != 0)
                            lastParam = commandQueue.Dequeue();
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
                SuggestFileSystemEntries(commandline, CommandInfo.ArgumentType.CommandName)
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

    [GeneratedRegex(@"(?<!\\) ")]
    private static partial Regex CommandLineWordsRegex();
}