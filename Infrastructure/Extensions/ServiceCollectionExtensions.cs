using Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddKernelWithProvider(configuration);

            services.AddTransient<YouTubeDownloader>();
            services.AddTransient<WhisperTranscriber>();
            services.AddTransient<RecipeParser>();
            services.AddTransient<LlmService>();

            return services;
        }

        private static IServiceCollection AddKernelWithProvider(this IServiceCollection services, IConfiguration config)
        {
            string providerName = config["LLM:Provider"] ?? "OpenAI";

            ILLMProvider provider = providerName switch
            {
                "OpenAI" => new OpenAiProvider(),
                _ => throw new InvalidOperationException($"Неизвестный LLM-провайдер: {providerName}.")
            };

            provider.Register(services, config);

            return services;
        }
    }
}