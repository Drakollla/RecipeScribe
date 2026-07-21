using Core.Contracts;
using Infrastructure.Database;
using Infrastructure.Exporters;
using Infrastructure.Providers;
using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeScribe.Infrastructure.Database;

namespace Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseServices(configuration);
        services.AddLlmServices(configuration);

        services.AddTransient<IVideoDownloader, YouTubeDownloader>();
        services.AddTransient<ITranscriber, WhisperTranscriber>();
        services.AddTransient<IRecipeExporter, MarkdownRecipeExporter>();

        services.AddScoped<IMealPlannerService, MealPlannerService>();
        services.AddScoped<IRecipeRepository, RecipeRepository>();
        services.AddTransient<IRecipeExtractorService, RecipeExtractorService>();

        return services;
    }

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
    {
        //string connectionString = configuration.GetConnectionString("DefaultConnection")
        //    ?? throw new InvalidOperationException("Строка подключения 'DefaultConnection' не найдена.");

        services.AddDbContext<RecipeDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        //services.AddDbContext<RecipeDbContext>(options =>
        //    options.UseSqlServer(connectionString),
        //    ServiceLifetime.Transient,
        //    ServiceLifetime.Transient);

        return services;
    }

    private static IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
    {
        var llmSettings = configuration.GetSection("LlmSettings").Get<LlmSettings>() ?? new LlmSettings();
        services.AddSingleton(llmSettings);

        services.AddKernelWithProvider(configuration);

        services.AddTransient<IRecipeParser, RecipeParser>();
        services.AddTransient<IIngredientSubstitutor, LlmSubstitutionService>();

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