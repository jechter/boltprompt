using CliWrap;
using CliWrap.Buffered;
using LanguageModels;
using Microsoft.Extensions.DependencyInjection;
using NiceIO;

namespace boltprompt;

static class AISuggestor
{
    private static readonly Dictionary<string, (Task task, Suggestion[] suggestions)> Cache = new();

    public static event Action AIDescriptionLoaded = () => {};

    private static Task<string>? _directoryListing;
    
    static async Task<string> GetDirectoryListing()
    {
        var result = await Cli.Wrap("ls").WithArguments("-al").ExecuteBufferedAsync();
        return result.StandardOutput;
    }

    private static Task<string>? _osInfo;

    static async Task<string> GetOSInfo()
    {
        var result = await Cli.Wrap("sw_vers").ExecuteBufferedAsync();
        return result.StandardOutput;
    }

    class AISuggestion
    {
        private readonly string _request;

        public AISuggestion(string request)
        {
            _request = request;
        }
        
        [DescriptionForLanguageModel("function to invoke with proposed suggestions")]
        public bool ProvideSuggestions(string suggestion)
        {
            Logger.Log("AISuggestor",$"Received AI Suggestion: {suggestion}");
            Cache.TryGetValue(_request, out var cacheEntry);
            cacheEntry.suggestions = cacheEntry.suggestions.Append(new(suggestion)).ToArray();
            Cache[_request] = cacheEntry;
            AIDescriptionLoaded.Invoke();
            return true;
        }
    }
    
    private static async Task GetSuggestionsFromAI(CancellationToken cancellationToken, string request)
    {
        _directoryListing ??= GetDirectoryListing();
        _osInfo ??= GetOSInfo();
        await Task.WhenAll(_directoryListing, _osInfo);
        
        var fullRequest =
            $"""
             We want to help a user writing shell commands in the Terminal. The current shell is {Environment.GetEnvironmentVariable("SHELL")}.
             
             This is our runtime environment:
             
             {_osInfo.Result}
             
             This is the listing of the current working directory {NPath.CurrentDirectory}:
             
             {_directoryListing.Result}
             
             These were the last commands executed:
             
             {string.Join("\n\n", History.Commands.TakeLast(5).Select(cmd => $"{cmd.Commandline}{(cmd.AIPrompt != null ? $"\n# suggested from AI prompt: `{cmd.AIPrompt}`" : "")}"))}
             
             This is a list of all the available commands:
             
             { string.Join(" ", Suggestor.ExecutablesInPathEnvironment.Select(s => s.Text)) }
             
             Propose a shell command line which performs the following request:
             
             `{request}` 
             
             If there are multiple reasonable ways you can perform the request, you can propose multiple different command lines.
             
             Call the ProvideSuggestions function once for each proposed command line string.
             """;
        

        try
        {
            var model = AIService.ServiceProvider.GetRequiredService<ILanguageModel>();
            var suggestions = new AISuggestion(request);
            var chatRequest = new ChatRequest()
            {
                 Messages = [new ChatMessage("user", fullRequest)],
                 Functions = CSharpBackedFunctions.Create([suggestions])
            };
            Logger.Log("AISuggestor",$"Sent AI Request for {request}");

            var r = model.Execute(chatRequest, cancellationToken);
            await r.ReadCompleteMessagesAsync().ReadAll();

        }
        catch (Exception e)
        {
            Logger.Log("AISuggestor", $"Caught: {e}");
            throw;
        }
    }

    private static readonly Suggestion PendingSuggestion = new("") { Icon = "🤖", Description = "suggestions pending" };

    private static CancellationTokenSource? _cancellationTokenSource;
    public static Suggestion[] Suggest(string request)
    {
        if (!AIService.Available) 
            return [];
          
        if (request.Trim().Length == 0)
            return [];

        if (Cache.TryGetValue(request, out var result))
        {
            if (result.task.IsCanceled)
                Cache.Remove(request);
            else
                return result.task.IsCompletedSuccessfully ? result.suggestions : result.suggestions.Length != 0 ? result.suggestions : [PendingSuggestion];
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new();
        
        var task = GetSuggestionsFromAI(_cancellationTokenSource.Token, request);
        Cache[request] = (task, []);
        return [PendingSuggestion];
    }
}