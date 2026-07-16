using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using TelegramBot.Strategies;

namespace TelegramBot.Extensions
{
    public static class TgServiceCollectionExtensions
    {
        public static IServiceCollection AddTelegramServices(this IServiceCollection services, IConfiguration configuration)
        {
            ServiceCollectionExtensions.AddInfrastructureServices(services, configuration);

            string telegramToken = configuration["ApiKeys:Telegram"]
                ?? throw new InvalidOperationException("Токен Telegram не найден в конфигурации (ApiKeys:Telegram).");

            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramToken));

            services.AddTransient<TelegramRecipeFlow>();
            services.AddTransient<TelegramMealPlanFlow>();

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

            services.AddScoped<ICallbackQuery, ShowRecipeCallback>();
            services.AddScoped<ICallbackQuery, GetShoppingListCallback>();
            services.AddScoped<ICallbackQuery, PlanDateCallback>();
            services.AddScoped<ICallbackQuery, PreferencesNoneCallback>();
            services.AddScoped<ICallbackQuery, RegenerateAiCallback>();

            return services;
        }
    }
}