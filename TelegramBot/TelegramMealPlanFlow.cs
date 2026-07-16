using Core.Contracts;
using Core.Enums;
using Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot 
{ 
    public class TelegramMealPlanFlow
    {
        private readonly IMealPlannerService _mealPlannerService;
        private readonly ILogger<TelegramMealPlanFlow> _logger;

        public TelegramMealPlanFlow(IMealPlannerService mealPlannerService, ILogger<TelegramMealPlanFlow> logger)
        {
            _mealPlannerService = mealPlannerService;
            _logger = logger;
        }

        public async Task ShowTodayMenuAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var plan = await _mealPlannerService.GetPlanForDateAsync(chatId, today);

            if (plan == null || plan.Items.Count == 0)
            {
                await botClient.SendMessage(chatId, "У вас пока нет меню на сегодня. Наберите /plan_ai, чтобы составить его с помощью ИИ!", cancellationToken: cancellationToken);
                return;
            }

            string formattedMenu = FormatMenuToMarkdown(plan);
            var keyboard = GetMenuKeyboard(plan, isConfirmed: true);

            await botClient.SendMessage(chatId, formattedMenu, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }

        public async Task ProcessAiPlanningAsync(ITelegramBotClient botClient, long chatId, DateOnly targetDate, string preferences, CancellationToken cancellationToken)
        {
            var statusMessage = await botClient.SendMessage(chatId, "Подбираю подходящие рецепты через ИИ...", cancellationToken: cancellationToken);

            try
            {
                var plan = await _mealPlannerService.GenerateSmartPlanAsync(chatId, targetDate, preferences);

                string formattedMenu = FormatMenuToMarkdown(plan);
                var keyboard = GetMenuKeyboard(plan, isConfirmed: false);

                await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);
                await botClient.SendMessage(chatId, formattedMenu, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка генерации меню для {ChatId}", chatId);
                await botClient.EditMessageText(chatId, statusMessage.Id, $"Не удалось составить меню: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        public string FormatMenuToMarkdown(MealPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"*МЕНЮ НА СЕГОДНЯ ({plan.Date:dd.MM.yyyy})*");
            sb.AppendLine();

            foreach (var item in plan.Items.OrderBy(i => i.MealType))
            {
                string mealName = item.MealType switch
                {
                    MealType.Breakfast => "ЗАВТРАК",
                    MealType.Lunch => "ОБЕД",
                    MealType.Dinner => "УЖИН",
                    _ => "ПЕРЕКУС"
                };

                sb.AppendLine($"*{mealName}:*");
                sb.AppendLine($"_{item.Recipe.Title}_");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public InlineKeyboardMarkup GetMenuKeyboard(MealPlan plan, bool isConfirmed)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            var recipeButtons = new List<InlineKeyboardButton>();
            
            foreach (var item in plan.Items.OrderBy(i => i.MealType))
            {
                string icon = item.MealType switch
                {
                    MealType.Breakfast => "Завтрак",
                    MealType.Lunch => "Обед",
                    MealType.Dinner => "Ужин",
                    _ => "Рецепт"
                };
                recipeButtons.Add(InlineKeyboardButton.WithCallbackData(icon, $"show_recipe:{item.RecipeId}"));
            }

            if (recipeButtons.Any())
                buttons.Add(recipeButtons);

            buttons.Add(
            [
                InlineKeyboardButton.WithCallbackData("Перегенерировать", "regenerate_ai"),
                InlineKeyboardButton.WithCallbackData("Список покупок", $"shopping_list:{plan.Id}")
            ]);

            return new InlineKeyboardMarkup(buttons);
        }

        public bool TryParseDate(string text, out DateOnly date)
        {
            text = text.Trim();

            if (DateOnly.TryParse(text, out date))
                return true;

            if (text.Contains('.') && text.Split('.').Length == 2)
            {
                if (DateOnly.TryParse($"{text}.{DateTime.Today.Year}", out date))
                    return true;
            }

            return false;
        }
    }
}
