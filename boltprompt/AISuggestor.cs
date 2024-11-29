using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;
using NiceIO;

namespace boltprompt;

static class AISuggestor
{
    private static readonly Dictionary<string, (Task task, Suggestion[] suggestions)> Cache = new();

    public static event Action AIDescriptionLoaded = () => {};

    private static Task<string>? _directoryListing;
    
    const string LogFile = "AISuggestor";

    public static string[] DefaultPromptSuggestions =>
    [
        " what is my IP address?",
        " count all lines in my source files",
        " find all files containing the word 'foo'",
        " which ports are open on localhost?",
        " open the github project web page for the current folder",
    ];
    
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
    
    private static void AddSuggestionToCache(string request, Suggestion suggestionEntry)
    {
        Cache.TryGetValue(request, out var cacheEntry);
        cacheEntry.suggestions = cacheEntry.suggestions.Append(suggestionEntry).ToArray();
        Cache[request] = cacheEntry;
        AIDescriptionLoaded.Invoke();
    }
    
    private static async Task GetSuggestionsFromAI(CancellationToken cancellationToken, string request)
    {
        var delayTask = Task.Delay(Configuration.Instance.DelayBeforeAskingAI, cancellationToken);
        _directoryListing ??= GetDirectoryListing();
        _osInfo ??= GetOSInfo();
        await Task.WhenAll(_directoryListing, _osInfo, delayTask);
        
        cancellationToken.ThrowIfCancellationRequested();

        var personalEnvironmentContext = Configuration.Instance.RemovePersonalInformationFromAIQueries
            ? ""
            : $"""
             The current shell is {Environment.GetEnvironmentVariable("SHELL")}.
             
             This is our runtime environment:
             
             {_osInfo.Result}
             
             This is the listing of the current working directory {NPath.CurrentDirectory}:
             
             {_directoryListing.Result}
             
             These were the last commands executed:
             
             {string.Join("\n\n", History.Commands.TakeLast(5).Select(cmd => $"{cmd.Commandline}{(cmd.AIPrompt != null ? $"\n# suggested from AI prompt: `{cmd.AIPrompt}`" : "")}"))}
             
             This is a list of all the available commands:
             
             { string.Join(" ", Suggestor.ExecutablesInPathEnvironment.Select(s => s.Text)) }
             
             """;
        
        var fullRequest =
            $"""
             We want to help a user writing shell commands in the Terminal. 
             
             {personalEnvironmentContext}
             Propose a shell command line which performs the following request:
             
             `{request}` 
             
             If there are multiple reasonable ways you can perform the request, you can propose multiple different command lines.
             
             Call the ProvideSuggestions function once for each proposed command line string.
             """;
        

        try
        {
            await AIService.RequestWithFunctions(fullRequest, cancellationToken, ProvideSuggestions);
        }
        catch (Exception e)
        {
            AddSuggestionToCache(request, FailureSuggestion);
            Logger.Log(LogFile, $"Caught exception: {e}");
            throw;
        }
        
        [Description("function to invoke with proposed suggestions")]
        bool ProvideSuggestions(
            [Description("the proposed command line")]
            string suggestion, 
            [Description("a one-line summary of how the command works")]
            string description)
        {
            Logger.Log(LogFile,$"Received AI Suggestion: {suggestion}");
            if (string.IsNullOrWhiteSpace(suggestion)) return true;
            
            var suggestionEntry = new Suggestion(suggestion) { Description = description, Icon = "🤖" };
            AddSuggestionToCache(request, suggestionEntry);
            return true;
        }
    }

    private static readonly Suggestion PendingSuggestion = new("") { Icon = "🤖", Description = "suggestions pending" };

    private static readonly Suggestion UnavailableSuggestion = new("") { Icon = "❌", Description = "AI suggestions unavailable. Set up your OpenAI key using 'boltprompt config set OpenAiApiKey'" };

    private static readonly Suggestion FailureSuggestion = new("") { Icon = "❌", Description = $"AI suggestions failed. See '{Logger.GetLogPath(LogFile)}' for details." };

    private static CancellationTokenSource? _cancellationTokenSource;
    public static Suggestion[] Suggest(string request)
    {
        if (!AIService.Available) 
            return [UnavailableSuggestion];
          
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