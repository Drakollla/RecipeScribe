using Shared.DTOs;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Contracts;

namespace TelegramBot;

public class TelegramMealPlanFlow
{
    private readonly IMealPlanApiClient _planApi;
    private readonly ILogger<TelegramMealPlanFlow> _logger;

    public TelegramMealPlanFlow(IMealPlanApiClient planApi, ILogger<TelegramMealPlanFlow> logger)
    {
        _planApi = planApi;
        _logger = logger;
    }

    public async Task ShowTodayMenuAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var plan = await _planApi.GetPlanAsync(chatId, today, cancellationToken);

            if (plan == null || plan.Items.Count == 0)
            {
                await botClient.SendMessage(chatId, "В базе нет меню на сегодня. Используй /plan_ai, чтобы создать меню на сегодня!", cancellationToken: cancellationToken);
                return;
            }

            string formattedMenu = FormatMenuToMarkdown(plan);
            var keyboard = GetMenuKeyboard(plan, isConfirmed: true);

            await botClient.SendMessage(chatId, formattedMenu, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API error getting meal plan for {ChatId}", chatId);
            await botClient.SendMessage(chatId, $"Ошибка загрузки меню: {ex.Message}", cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке меню для {ChatId}", chatId);
            await botClient.SendMessage(chatId, "Произошла ошибка при загрузке меню.", cancellationToken: cancellationToken);
        }
    }

    public async Task ProcessAiPlanningAsync(ITelegramBotClient botClient, long chatId, DateOnly targetDate, string preferences, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "Генерирую индивидуальное меню через ИИ...", cancellationToken: cancellationToken);

        try
        {
            var plan = await _planApi.GeneratePlanAsync(chatId, targetDate, preferences, cancellationToken);

            string formattedMenu = FormatMenuToMarkdown(plan);
            var keyboard = GetMenuKeyboard(plan, isConfirmed: false);

            await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(chatId, formattedMenu, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка генерации меню для {ChatId}", chatId);
            await botClient.EditMessageText(chatId, statusMessage.Id, "Не удалось составить меню из-за технической ошибки.", cancellationToken: cancellationToken);
        }
    }

    public string FormatMenuToMarkdown(MealPlanDto plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>Меню на сегодня ({plan.Date})</b>");
        sb.AppendLine();

        foreach (var item in plan.Items)
        {
            sb.AppendLine($"<b>{HtmlHelper.Escape(item.MealType.ToUpper())}:</b>");
            sb.AppendLine($"<i>{HtmlHelper.Escape(item.Recipe.Title)}</i>");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public InlineKeyboardMarkup GetMenuKeyboard(MealPlanDto plan, bool isConfirmed)
    {
        var buttons = new List<List<InlineKeyboardButton>>();
        var recipeButtons = new List<InlineKeyboardButton>();

        foreach (var item in plan.Items)
        {
            recipeButtons.Add(InlineKeyboardButton.WithCallbackData(item.MealType, $"show_recipe:{item.Recipe.Id}"));
        }

        if (recipeButtons.Any())
            buttons.Add(recipeButtons);

        buttons.Add(
        [
            InlineKeyboardButton.WithCallbackData("Сгенерировать заново", "regenerate_ai"),
            InlineKeyboardButton.WithCallbackData("Список покупок", $"shopping_list:{plan.Id}")
        ]);

        return new InlineKeyboardMarkup(buttons);
    }

    public bool TryParseDate(string text, out DateOnly date)
    {
        text = text.Trim();

        date = default;
        var formats = new[] { "dd.MM.yyyy", "dd.MM", "yyyy-MM-dd" };
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        if (DateOnly.TryParseExact(text, formats, culture, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            date = parsed.Year < 100
                ? new DateOnly(DateTime.Today.Year, parsed.Month, parsed.Day)
                : parsed;
            return true;
        }

        return false;
    }
}
