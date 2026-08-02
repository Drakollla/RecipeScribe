using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Infrastructure.Providers;

public class OpenAiProvider : ILLMProvider
{
    public string Name => "OpenAI";

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddKernel();
        services.AddSingleton<IChatCompletionService>(sp =>
            new DynamicChatCompletionService(config, sp.GetRequiredService<ILoggerFactory>()));
    }
}
