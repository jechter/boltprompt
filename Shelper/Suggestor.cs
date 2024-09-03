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
    private Suggestion[] executablesInPath;
    private string[] history;

    Suggestion? GetExecutableCommandInfo(string command) =>
        executablesInPath.FirstOrDefault(exe => exe.Text.Trim() == Path.GetFileName(command));
    
    public Suggestor()
    {
        history = History.GetCommands();
        executablesInPath = FindExecutablesInPath();
        KnownCommands.CommandInfoLoaded += ci =>
        {
            var sug = GetExecutableCommandInfo(ci.Name);
            if (sug != null)
                sug.Description = ci.Description;
        };
    }
    
    public Suggestion[] SuggestionsForPrompt(string commandline)
    {
        var split = commandline.Split(' ');
        var command = split[0];
        return split.Length == 1 ? SuggestCommand(command) : SuggestParameters(command, commandline).ToArray();
    }

    private static Suggestion[] SuggestFileSystemEntries(string commandline, bool directoriesOnly)
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

        return new []{dir}.Concat(directoriesOnly ? dir.Directories() : dir.Contents())
            .Select(fs => new Suggestion($"{prefix}{fs.RelativeTo(dir)}{(fs.DirectoryExists()?'/':' ')}") { Icon = fs.DirectoryExists()?"📁" : "📄"})
            .ToArray();
    }
    
    private IEnumerable<Suggestion> SuggestParameters(string command, string commandline)
    {
        var executableExists = GetExecutableCommandInfo(command) != null;
        var ci = KnownCommands.GetCommand(command, executableExists, out var pending) ?? CommandInfo.DefaultCommand;
        if (pending)
            ci = ci with { Description = "pending..." };
        if (ci?.Arguments == null)
            yield break;
        var lastParam = commandline.Split(' ').Last();
        var arguments = ci.Arguments.SelectMany(a => a).ToList();
        var keepParsing = true;
        var paramPrefix = "";
        CommandInfo.Argument? curArg = null;
        while (keepParsing)
        {
            keepParsing = false;
            foreach (var arg in arguments)
            {
                if (arg.Type != CommandInfo.ArgumentType.Keyword && arg.Type != CommandInfo.ArgumentType.Flag) continue;
                foreach (var v in arg.AllNames)
                {
                    var name = v;
                    if (paramPrefix == "")
                        name = $"-{v}";
                    if (!lastParam.StartsWith(name)) continue;
                    if (arg.Arguments != null)
                    {
                        arguments = arg.Arguments.ToList();
                        paramPrefix += lastParam[..name.Length];
                        lastParam = lastParam[name.Length..];
                        curArg = arg;
                        keepParsing = true;
                    }
                    else if (arg.Type == CommandInfo.ArgumentType.Flag)
                    {
                        arguments = arguments.Where(a => a.Type == CommandInfo.ArgumentType.Flag && (a != arg || arg.Repeat)).ToList();
                        paramPrefix += lastParam[..name.Length];
                        lastParam = lastParam[name.Length..];
                        curArg = arg;
                        keepParsing = true;
                    }
                    /*else if (arg.Repeat)
                    {
                        paramPrefix += lastParam[..name.Length];
                        lastParam = lastParam[name.Length..];
                        curArg = arg;
                        keepParsing = true;
                    }*/
                }
            }
        }

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
                    foreach (var s in executablesInPath.Where(sug => sug.Text.StartsWith(lastParam)))
                        yield return s;

                    break;
                }
                case CommandInfo.ArgumentType.FileSystemEntry:
                case CommandInfo.ArgumentType.Directory:
                case CommandInfo.ArgumentType.File:
                {
                    foreach (var s in SuggestFileSystemEntries(commandline, arg.Type == CommandInfo.ArgumentType.Directory))
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

    private Suggestion[] SuggestCommand(string commandline)
    {
        if (string.IsNullOrEmpty(commandline))
            return history.Select(h => new Suggestion(h)).ToArray();
        return executablesInPath.Concat(history.Select(h => new Suggestion(h)))
            .Where(sug => sug.Text.StartsWith(commandline))
            .ToArray();
    }

    private static Suggestion[] FindExecutablesInPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        var paths = pathEnv != null 
            ? new[] { cwd }.Concat(pathEnv.Split(":")) 
            : Array.Empty<string>();
        var executables = paths
            .Where(Directory.Exists)
            .SelectMany(Directory.GetFiles)
            .Where(IsExecutable);
        return executables.Select(ex => new Suggestion($"{Path.GetFileName(ex)} ")
        {
            Description = KnownCommands.GetCommand(Path.GetFileName(ex), false, out _)?.Description ?? ex
        })
            .OrderBy(s => s.Text)
            .ToArray();

        bool IsExecutable(string path)
        {
            if (Syscall.stat(path, out var fileStat) == 0)
                return (fileStat.st_mode & FilePermissions.S_IXUSR) != 0;
            return false;
        }
    }
}