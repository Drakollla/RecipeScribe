using Core.Contracts;
using Infrastructure.Providers;
using Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var llmSettings = configuration.GetSection("LlmSettings").Get<LlmSettings>() ?? new LlmSettings();
            services.AddSingleton(llmSettings);

            services.AddKernelWithProvider(configuration);

            services.AddTransient<IVideoDownloader, YouTubeDownloader>();
            services.AddTransient<ITranscriber, WhisperTranscriber>();
            services.AddTransient<IRecipeParser, RecipeParser>();

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