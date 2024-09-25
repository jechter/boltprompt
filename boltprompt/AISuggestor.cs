namespace boltprompt;

static class AISuggestor
{
    private static readonly Dictionary<string, Task<Suggestion[]>> Cache = new();

    public static event Action AIDescriptionLoaded = () => {};

    private static async Task<Suggestion[]> GetSuggestionsFromAI(string request)
    {
        var fullRequest =
            $"Propose a shell command line which performs the following request:\n`{request}`. If there are multiple reasonable ways you can perform the request, you can reply with multiple command lines - one per line. Reply with the command line(s) only - no further text!";
        var reply = await ChatGptClient.GetReply(fullRequest);
        return reply.Split(Environment.NewLine).Where(line => !line.StartsWith("```")).Select(line => new Suggestion(line)).ToArray();
    }

    private static readonly Suggestion PendingSuggestion = new("") { Icon = "🧠", Description = "suggestions pending" };
    public static Suggestion[] Suggest(string request)
    {
        if (!ChatGptClient.IsAvailable)
            return [];

        if (request.Trim().Length == 0)
            return [];
        
        if (Cache.TryGetValue(request, out var result)) 
            return result.IsCompletedSuccessfully ? result.Result : [PendingSuggestion];

        var task = GetSuggestionsFromAI(request);
        Cache[request] = task;
        task.ContinueWith(_ => AIDescriptionLoaded.Invoke());
        return [PendingSuggestion ];
    }
}