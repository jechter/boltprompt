using Microsoft.Extensions.AI;
using OpenAI;

namespace boltprompt;

class AIService
{
    public static bool Available => Configuration.Instance.OpenAiApiKey != null;

    private static IChatClient ChatClient => Instance._chatClient ?? throw new NullReferenceException();
    private readonly IChatClient? _chatClient;
        
    private AIService()
    {
        if (!Available) return;
        var openAiClient = new OpenAIClient(Configuration.Instance.OpenAiApiKey)
            .AsChatClient(modelId: "gpt-4o-mini");
        _chatClient = new ChatClientBuilder(openAiClient)
            .UseFunctionInvocation()
            .Build();
    }
 
    private static readonly AIService Instance = new (); 

    public static Task<ChatCompletion> RequestWithFunctions(string request, CancellationToken cancellationToken, params Delegate[] functions) =>
        ChatClient.CompleteAsync(
                request,
                new()
                {
                    Tools = functions.Select(f => AIFunctionFactory.Create(f)).Cast<AITool>().ToArray()
                },
                cancellationToken: cancellationToken
            );
    
    public static IAsyncEnumerable<StreamingChatCompletionUpdate> RequestWithFunctionsStreaming(string request, CancellationToken cancellationToken, params Delegate[] functions) =>
        ChatClient.CompleteStreamingAsync(
            request,
            new()
            {
                Tools = functions.Select(f => AIFunctionFactory.Create(f)).Cast<AITool>().ToArray()
            },
            cancellationToken: cancellationToken
        );
}