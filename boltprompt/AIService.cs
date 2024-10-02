using LanguageModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace boltprompt;

class AIService
{
    readonly ServiceProvider _serviceProvider;
    public static bool Available => Instance.IsAvailable();
    public static ILanguageModel LanguageModel => Instance._serviceProvider.GetRequiredService<ILanguageModel>();

    private IConfiguration Configuration { get; } = new ConfigurationBuilder().AddUserSecrets<AIService>().AddEnvironmentVariables().Build();

    private AIService()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(Configuration);
        serviceCollection.AddLanguageModels();
        serviceCollection.AddScoped<ILanguageModel>(sp => ActivatorUtilities.CreateInstance<OpenAIModels>(sp).Gpt4oMini);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    private bool IsAvailable() => Configuration["OPENAI_API_KEY"] != null;

    private static readonly AIService Instance = new ();
}