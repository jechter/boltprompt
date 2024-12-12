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
    private static Task<string>? _osInfo;
    private static Task<string>? _terminalCapture;
    
    const string LogFile = "AISuggestor";

    public static string[] DefaultPromptSuggestions =>
    [
        " what is my IP address?",
        " count all lines in my source files",
        " find all files containing the word 'foo'",
        " which ports are open on localhost?",
        " open the github project web page for the current folder"
    ];
    
    public static string[] DefaultQuestionSuggestions =>
    [
        "",
        " for what can I use the terminal",
        " how do pipe operators work",
        " how do I find out which process is using the most cpu time",
        " how do I set up a git repository",
        " should I use emacs or vi",
    ];

    private static async Task<string> GetDirectoryListing()
    {
        var result = await Cli.Wrap("ls").WithArguments("-al").ExecuteBufferedAsync();
        return result.StandardOutput;
    }
    
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
    
    private static async Task<string> GetPersonalEnvironmentContext(CancellationToken cancellationToken)
    {
        _directoryListing ??= GetDirectoryListing();
        _osInfo ??= GetOSInfo();
        _terminalCapture ??= TerminalUtility.GetCurrentTerminalBuffer();
        await Task.WhenAll(_directoryListing, _osInfo, _terminalCapture);
        
        cancellationToken.ThrowIfCancellationRequested();

        var terminalCapture = string.IsNullOrEmpty(_terminalCapture.Result) ? "" : $"These are the last lines from the current terminal session:\n\n```{_terminalCapture.Result}\n```";
        return Configuration.Instance.RemovePersonalInformationFromAIQueries
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

               {string.Join(" ", Suggestor.ExecutablesInPathEnvironment.Select(s => s.Text))}
               {terminalCapture}

               """;
    }

    private static async Task GetSuggestionsFromAI(CancellationToken cancellationToken, string request)
    {
        var delayTask = Task.Delay(Configuration.Instance.DelayBeforeAskingAI, cancellationToken);
        var personalEnvironmentContext = GetPersonalEnvironmentContext(cancellationToken);
        await Task.WhenAll(personalEnvironmentContext, delayTask);
        cancellationToken.ThrowIfCancellationRequested();
        
        var fullRequest =
            $"""
             We want to help a user writing shell commands in the Terminal. 
             
             {personalEnvironmentContext.Result}
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

        return;

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


    public static async Task RespondToAIQuestion(string request, CancellationToken cancellationToken)
    {
        var fullRequest =
            $"""
             We want to help a user using the Terminal. 

             {await GetPersonalEnvironmentContext(cancellationToken)}
            """;
        if (string.IsNullOrWhiteSpace(request))
            fullRequest +=
                "\nThe user needs help, but did not specify how. Please just explain what is going on in the last lines of the terminal session. If there were any error messages in the terminal output, explain how to fix them.";
        else
            fullRequest += 
                $"""
                 The user would like an answer to the following question:
                 
                 {request}
                 """;
        
        Logger.Log(LogFile,fullRequest);
        var chatResult = AIService.RequestWithFunctionsStreaming(fullRequest, cancellationToken);
        var bold = false;
        var headline = false;
        var monospace = false;
        var codeBlock = false;
        var textBuffer = "";
        var resetBoldAndUnderline = BufferedConsole.BoldEsc(false) + BufferedConsole.UnderlineEsc(false);

        await foreach (var message in chatResult)
        {
            if (message.Text != null)
                PrintChatResponseFormatted(message.Text);
        }

        PrintChatResponseFormatted("\n");
        Console.WriteLine();
        return;

        void PrintChatResponseFormatted(string text)
        {
            if (!text.Contains('\n'))
            {
                textBuffer += text;
                return;
            }

            text = textBuffer + text;
            textBuffer = "";

            var pos = 0;
            if (headline)
            {
                if (ReplaceKey("\n", resetBoldAndUnderline, ref pos))
                    headline = false;
            }

            if (!headline)
            {
                while (ReplaceKey("###", $"{BufferedConsole.BoldEsc(true)}###", ref pos))
                {
                    if (!ReplaceKey("\n", resetBoldAndUnderline, ref pos))
                        headline = true;
                }
                
                while (ReplaceKey("##", $"{BufferedConsole.BoldEsc(true)}##", ref pos))
                {
                    if (!ReplaceKey("\n", resetBoldAndUnderline, ref pos))
                        headline = true;
                }
                
                while (ReplaceKey("#", $"{BufferedConsole.BoldEsc(true)}{BufferedConsole.UnderlineEsc(true)}#", ref pos))
                {
                    if (!ReplaceKey("\n", resetBoldAndUnderline, ref pos))
                        headline = true;
                }
            }

            pos = 0;
            while (ReplaceKey("**", BufferedConsole.BoldEsc(!bold), ref pos))
                bold = !bold;
            pos = 0;
            while (ReplaceKey("__", BufferedConsole.BoldEsc(!bold), ref pos))
                bold = !bold;
            pos = 0;
            var bg = BufferedConsole.ParseHtmlColor(Configuration.Instance.SuggestionBackgroundColor);
            var bgDark = (r: (byte)(bg.r * 0.8), g: (byte)(bg.g * 0.8), b: (byte)(bg.b * 0.8));
            while (ReplaceKey("```", !codeBlock ? 
                       $"{BufferedConsole.MoveToStartOfLineEsc()}{BufferedConsole.ForegroundColorEsc(Configuration.Instance.SuggestionTextColor)}{BufferedConsole.BackgroundColorEsc(bgDark)}📜 " 
                       : $"{BufferedConsole.ClearEndOfLineEsc()}{BufferedConsole.ResetColorEsc()}", 
                       ref pos))
                codeBlock = !codeBlock;
            pos = 0;
            while (ReplaceKey("`", !monospace ? BufferedConsole.ForegroundColorEsc(BufferedConsole.ConsoleColor.Gray16) : BufferedConsole.ResetColorEsc(), ref pos))
                monospace = !monospace;
            pos = 0;
            if (codeBlock)
                ReplaceKey("\n", $"{BufferedConsole.ClearEndOfLineEsc()}\n{BufferedConsole.BackgroundColorEsc(bg)}", ref pos);
                

            Console.Write(text);
            return;

            bool ReplaceKey(string key, string value, ref int searchPos)
            {
                var i = text.IndexOf(key, searchPos, StringComparison.InvariantCulture);
                if (i == -1) return false;
                text = text[..i] + value + text[(i + key.Length)..];
                searchPos = i + value.Length;
                return true;
            }
        }
    }
}