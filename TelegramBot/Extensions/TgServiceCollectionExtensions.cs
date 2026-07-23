using Telegram.Bot;
using TelegramBot.ApiClients;
using TelegramBot.Contracts;
using TelegramBot.Strategies;

namespace TelegramBot.Extensions
{
    public static class TgServiceCollectionExtensions
    {
        public static IServiceCollection AddTelegramServices(this IServiceCollection services, IConfiguration configuration)
        {
            AddHttpClient(services, configuration);
            AddTelegramBotClient(services, configuration);
            AddApiClients(services);
            AddFlows(services);
            AddCommands(services);
            AddCallbacks(services);

            return services;
        }

        private static void AddHttpClient(IServiceCollection services, IConfiguration configuration)
        {
            var apiBaseUrl = configuration["Api:BaseUrl"]
                ?? throw new InvalidOperationException("Api:BaseUrl is not configured. Set it in appsettings.json or user secrets.");

            services.AddHttpClient("RecipeScribeApi", client =>
            {
                client.BaseAddress = new Uri(apiBaseUrl);
                client.Timeout = TimeSpan.FromMinutes(5);
            });
        }

        private static void AddTelegramBotClient(IServiceCollection services, IConfiguration configuration)
        {
            string telegramToken = configuration["ApiKeys:Telegram"]
                ?? throw new InvalidOperationException("Telegram token is not configured (ApiKeys:Telegram).");

            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));
        }

        private static void AddApiClients(IServiceCollection services)
        {
            services.AddTransient<IRecipeApiClient>(sp => CreateApiClient<RecipeApiClient>(sp));
            services.AddTransient<IMealPlanApiClient>(sp => CreateApiClient<MealPlanApiClient>(sp));
            services.AddTransient<ISubstitutionApiClient>(sp => CreateApiClient<SubstitutionApiClient>(sp));
            services.AddTransient<IExportApiClient>(sp => CreateApiClient<ExportApiClient>(sp));
            services.AddTransient<IUserApiClient>(sp => CreateApiClient<UserApiClient>(sp));
        }

        private static T CreateApiClient<T>(IServiceProvider sp) where T : class
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("RecipeScribeApi");
            var logger = sp.GetRequiredService<ILogger<T>>();
            return ActivatorUtilities.CreateInstance<T>(sp, http, logger);
        }

        private static void AddFlows(IServiceCollection services)
        {
            services.AddTransient<TelegramRecipeFlow>();
            services.AddTransient<TelegramMealPlanFlow>();
        }

        private static void AddCommands(IServiceCollection services)
        {
            services.AddScoped<IMessageCommand, StartCommand>();
            services.AddScoped<IMessageCommand, HelpCommand>();
            services.AddScoped<IMessageCommand, CancelCommand>();
            services.AddScoped<IMessageCommand, MenuCommand>();
            services.AddScoped<IMessageCommand, PlanAiCommand>();
            services.AddScoped<IMessageCommand, CustomDateMessageCommand>();
            services.AddScoped<IMessageCommand, SearchCommand>();
            services.AddScoped<IMessageCommand, SearchIngredientsCommand>();
            services.AddScoped<IMessageCommand, RecipeUrlCommand>();
            services.AddScoped<IMessageCommand, AiPreferencesMessageCommand>();
            services.AddScoped<IMessageCommand, SubstituteCommand>();
            services.AddScoped<IMessageCommand, SubstituteIngredientCommand>();
            services.AddScoped<IMessageCommand, SubstituteRecipeCommand>();
            services.AddScoped<IMessageCommand, ScaleServingsCommand>();
            services.AddScoped<IMessageCommand, SettingsCommand>();
            services.AddScoped<IMessageCommand, SettingsValueCommand>();
        }

        private static void AddCallbacks(IServiceCollection services)
        {
            services.AddScoped<ICallbackQuery, ShowRecipeCallback>();
            services.AddScoped<ICallbackQuery, GetShoppingListCallback>();
            services.AddScoped<ICallbackQuery, PlanDateCallback>();
            services.AddScoped<ICallbackQuery, PreferencesNoneCallback>();
            services.AddScoped<ICallbackQuery, RegenerateAiCallback>();
            services.AddScoped<ICallbackQuery, ExportToObsidianCallback>();
            services.AddScoped<ICallbackQuery, ScaleCallback>();
        }
    }
}
