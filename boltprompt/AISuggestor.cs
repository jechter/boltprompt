using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace boltprompt;

static class AISuggestor
{
    private static readonly Dictionary<string, Task<Suggestion[]>> Cache = new();

    public static event Action AIDescriptionLoaded = () => {};

    private static Task<string>? _directoryListing = null;

    static async Task<string> GetDirectoryListing()
    {
        var result = await Cli.Wrap("ls").WithArguments("-al").ExecuteBufferedAsync();
        return result.StandardOutput;
    }

    private static async Task<Suggestion[]> GetSuggestionsFromAI(CancellationToken cancellationToken, string request)
    {
        _directoryListing ??= GetDirectoryListing();
        await _directoryListing;
        var fullRequest =
            $"""
             We want to help a user writing shell commands in the Terminal. The current shell is {Environment.GetEnvironmentVariable("SHELL")}.
             This is the listing of the current working directory {NPath.CurrentDirectory}:
             
             {_directoryListing.Result}
             
             These were the last commands executed:
             
             {History.Commands.TakeLast(5)}
             
             This is a list of all the available commands:
             
             { string.Join(" ", Suggestor.ExecutablesInPathEnvironment.Select(s => s.Text)) }
             
             Propose a shell command line which performs the following request:
             
             `{request}` 
             
             If there are multiple reasonable ways you can perform the request, you can reply with multiple command lines - one per line. 
             Reply with the command line(s) only - no further text!
             """;
        var reply = await ChatGptClient.GetReply(cancellationToken, fullRequest);
        return reply.Split(Environment.NewLine).Where(line => !line.StartsWith("```")).Select(line => new Suggestion(line)).ToArray();
    }

    private static readonly Suggestion PendingSuggestion = new("") { Icon = "🧠", Description = "suggestions pending" };


    static CancellationTokenSource? _cancellationTokenSource;
    public static Suggestion[] Suggest(string request)
    {
        if (!ChatGptClient.IsAvailable)
            return [];

        if (request.Trim().Length == 0)
            return [];

        if (Cache.TryGetValue(request, out var result))
        {
            if (result.IsCanceled)
                Cache.Remove(request);
            else
                return result.IsCompletedSuccessfully ? result.Result : [PendingSuggestion];
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new();
        
        var task = GetSuggestionsFromAI(_cancellationTokenSource.Token, request);
        Cache[request] = task;
        task.ContinueWith(_ => AIDescriptionLoaded.Invoke());
        return [PendingSuggestion];
    }
}