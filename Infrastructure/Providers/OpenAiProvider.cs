using Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Infrastructure.Providers
{
    public class OpenAiProvider : ILLMProvider
    {
        public string Name => "OpenAI";

        public void Register(IServiceCollection services, IConfiguration config)
        {
            var llmSettings = config.GetSection("LlmSettings").Get<LlmSettings>() ?? new LlmSettings();
            var apiKey = config["ApiKeys:Llm"] ?? "ollama";

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(llmSettings.Endpoint),
                Timeout = TimeSpan.FromMinutes(5)
            };

            services.AddKernel()
                .AddOpenAIChatCompletion(
                    modelId: llmSettings.ModelId,
                    apiKey: apiKey,
                    httpClient: httpClient
                );
        }
    }
}
