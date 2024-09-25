using CliWrap;
using CliWrap.Buffered;

namespace boltprompt;

static class CustomArguments
{
    private static readonly Dictionary<string, Task<Suggestion[]>> CustomArgumentCache = new();

    public static event Action CustomArgumentsLoaded = () => {};

    private static async Task<Suggestion[]> GetAsync(string command)
    {
        var commandSplit = Suggestor.SplitCommandIntoWords(command);
        var result = await Cli.Wrap(commandSplit[0]).WithArguments(commandSplit.Skip(1)).ExecuteBufferedAsync();
        var lines = result.StandardOutput.Split("\n", StringSplitOptions.RemoveEmptyEntries);
        return lines.Select(line => new Suggestion(line)).ToArray();
    }
    
    public static Suggestion[] Get(string command)
    {
        if (CustomArgumentCache.TryGetValue(command, out var argumentsTask))
            return argumentsTask.IsCompleted ? argumentsTask.Result : [];

        CustomArgumentCache[command] = GetAsync(command);
        CustomArgumentCache[command].ContinueWith(_ => CustomArgumentsLoaded.Invoke());
        return [];
    }
}