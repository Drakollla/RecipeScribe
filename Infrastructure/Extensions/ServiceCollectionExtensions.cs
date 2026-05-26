using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddLlmServices(configuration);

            services.AddTransient<YouTubeDownloader>();
            services.AddTransient<WhisperTranscriber>();
            services.AddTransient<RecipeParser>();

            return services;
        }

        private static IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
        {
            var apiKey = configuration["ApiKeys:Groq"] ?? "ollama";

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.groq.com/openai/v1/"),
                Timeout = TimeSpan.FromMinutes(5)
            };

            services.AddKernel()
                .AddOpenAIChatCompletion(
                    modelId: "openai/gpt-oss-20b",
                    apiKey: apiKey,
                    httpClient: httpClient
                );

            services.AddTransient<LlmService>();

            return services;
        }
    }
}