using Core.Contracts;
using Core.Enums;
using Core.Models;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ConsoleApp;

public enum BotState
{
    None,
    WaitingForAiPreferences
}

public class UserStateInfo
{
    public BotState State { get; set; } = BotState.None;
    public string LastPreferences { get; set; } = string.Empty;
}

public class TelegramBotService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IVideoDownloader _downloader;
    private readonly ITranscriber _transcriber;
    private readonly IRecipeParser _parser;
    private readonly IRecipeRepository _repository;
    private readonly IMealPlannerService _mealPlannerService;

    public TelegramBotService(ITelegramBotClient botClient,
        IVideoDownloader downloader,
        ITranscriber transcriber,
        IRecipeParser parser,
        IRecipeRepository repository,
        IMealPlannerService mealPlannerService)
    {
        _botClient = botClient;
        _downloader = downloader;
        _transcriber = transcriber;
        _parser = parser;
        _repository = repository;
        _mealPlannerService = mealPlannerService;
    }

    private readonly ConcurrentDictionary<long, UserStateInfo> _userStates = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _botClient.GetMe(stoppingToken);
        Console.WriteLine($"Бот @{me.Username} успешно запущен как BackgroundService.");

        await _botClient.SetMyCommands(
        [
            new BotCommand { Command = "menu", Description = "Показать меню на сегодня" },
            new BotCommand { Command = "plan_ai", Description = "Спланировать меню через ИИ" }
        ], cancellationToken: stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message is { } message)
            {
                await HandleMessageAsync(botClient, message, cancellationToken);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(botClient, callbackQuery, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Бот] Ошибка при обработке апдейта: {ex.Message}");
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        if (message.Text is not { } messageText)
            return;

        long chatId = message.Chat.Id;
        string text = messageText.Trim();

        Console.WriteLine($"[Бот] Получено сообщение от {chatId}: {text}");

        var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());

        if (stateInfo.State == BotState.WaitingForAiPreferences)
        {
            stateInfo.State = BotState.None;
            stateInfo.LastPreferences = text;
            _ = Task.Run(() => ProcessAiPlanningAsync(botClient, chatId, text, cancellationToken), cancellationToken);
            return;
        }

        if (text.StartsWith
            ('/'))
        {
            string command = text.ToLower().Split(' ')[0];
            switch (command)
            {
                case "/menu":
                    _ = Task.Run(() => ShowTodayMenuAsync(botClient, chatId, cancellationToken), cancellationToken);
                    return;

                case "/plan_ai":
                    stateInfo.State = BotState.WaitingForAiPreferences;
                    var keyboard = new InlineKeyboardMarkup(
                    [
                        [InlineKeyboardButton.WithCallbackData("Без предпочтений", "pref_none")]
                    ]);

                    await botClient.SendMessage(
                        chatId,
                        "Есть ли пожелания к меню (например, вегетарианское, быстрое, без рыбы)? Напишите их ответным сообщением или нажмите кнопку ниже:",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken
                    );
                    return;

                default:
                    await botClient.SendMessage(
                        chatId,
                        "Доступные команды:\n/menu — показать меню на сегодня\n/plan_ai — спланировать меню через ИИ\n\nИли просто отправьте ссылку на видео-рецепт!",
                        cancellationToken: cancellationToken
                    );
                    return;
            }
        }

        bool isUrl = Uri.TryCreate(text, UriKind.Absolute, out var uriResult)
                     && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

        if (!isUrl)
        {
            await botClient.SendMessage(chatId, "Пожалуйста, отправьте корректную ссылку или выберите команду из меню.", cancellationToken: cancellationToken);
            return;
        }

        _ = Task.Run(() => ProcessVideoRecipeAsync(botClient, chatId, text, cancellationToken), cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } message) 
            return;
        
        long chatId = message.Chat.Id;
        int messageId = message.Id;
        string data = callbackQuery.Data ?? string.Empty;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

        var stateInfo = _userStates.GetOrAdd(chatId, _ => new UserStateInfo());

        if (data == "pref_none")
        {
            stateInfo.State = BotState.None;
            stateInfo.LastPreferences = "без особых предпочтений";

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
            _ = Task.Run(() => ProcessAiPlanningAsync(botClient, chatId, "без особых предпочтений", cancellationToken), cancellationToken);
        }
        else if (data == "regenerate_ai")
        {
            string prefs = string.IsNullOrWhiteSpace(stateInfo.LastPreferences) ? "без особых предпочтений" : stateInfo.LastPreferences;

            await botClient.DeleteMessage(chatId, messageId, cancellationToken: cancellationToken);
            _ = Task.Run(() => ProcessAiPlanningAsync(botClient, chatId, prefs, cancellationToken), cancellationToken);
        }
        else if (data == "confirm_menu")
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var plan = await _mealPlannerService.GetPlanForDateAsync(chatId, today);

            if (plan != null)
            {
                string formattedMenu = FormatMenuToMarkdown(plan) + "\nМеню успешно подтверждено!";

                var keyboard = GetMenuKeyboard(plan, isConfirmed: true);

                await botClient.EditMessageText(chatId, messageId, formattedMenu, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
            }
        }
        else if (data.StartsWith("shopping_list:"))
        {
            string planIdStr = data.Split(':')[1];
            if (Guid.TryParse(planIdStr, out var planId))
            {
                string shoppingListMarkdown = await _mealPlannerService.GetShoppingListAsync(planId);

                await botClient.SendMessage(
                    chatId,
                    shoppingListMarkdown,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }
        else if (data.StartsWith("show_recipe:"))
        {
            string recipeIdStr = data.Split(':')[1];

            if (Guid.TryParse(recipeIdStr, out var recipeId))
            {
                var recipe = await _repository.GetRecipeByIdAsync(recipeId);

                if (recipe == null)
                {
                    await botClient.SendMessage(chatId, "Не удалось найти рецепт в базе данных.", cancellationToken: cancellationToken);
                    return;
                }

                string formattedRecipe = FormatRecipeToMarkdown(recipe);
                byte[] mdBytes = Encoding.UTF8.GetBytes(formattedRecipe);
                using var stream = new MemoryStream(mdBytes);
                string fileName = $"{recipe.Title.Replace(" ", "_")}.md";

                await botClient.SendDocument(
                    chatId: chatId,
                    document: InputFile.FromStream(stream, fileName),
                    caption: $"📖 *Рецепт:* _{recipe.Title}_",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private async Task ShowTodayMenuAsync(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
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

        await botClient.SendMessage(chatId, formattedMenu, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    private async Task ProcessAiPlanningAsync(ITelegramBotClient botClient, long chatId, string preferences, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "Подбираю подходящие рецепты через ИИ...", cancellationToken: cancellationToken);

        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var plan = await _mealPlannerService.GenerateSmartPlanAsync(chatId, today, preferences);

            string formattedMenu = FormatMenuToMarkdown(plan);
            var keyboard = GetMenuKeyboard(plan, isConfirmed: false);

            await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(chatId, formattedMenu, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка генерации меню для {chatId}: {ex.Message}");
            await botClient.EditMessageText(chatId, statusMessage.Id, $"❌ Не удалось составить меню: {ex.Message}", cancellationToken: cancellationToken);
        }
    }

    private string FormatMenuToMarkdown(MealPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*МЕНЮ НА СЕГОДНЯ ({plan.Date:dd.MM.yyyy})*");
        sb.AppendLine();

        foreach (var item in plan.Items)
        {
            string mealName = item.MealType switch
            {
                MealType.Breakfast => "ЗАВТРАК",
                MealType.Lunch => "ОБЕД",
                MealType.Dinner => " УЖИН",
                _ => "ПЕРЕКУС"
            };

            sb.AppendLine($"*{mealName}:*");
            sb.AppendLine($"_{item.Recipe.Title}_");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private InlineKeyboardMarkup GetMenuKeyboard(MealPlan plan, bool isConfirmed)
    {
        var buttons = new List<List<InlineKeyboardButton>>();

        if (!isConfirmed)
        {
            buttons.Add(
            [
                InlineKeyboardButton.WithCallbackData("Подтвердить", "confirm_menu"),
                InlineKeyboardButton.WithCallbackData("Перегенерировать", "regenerate_ai")
            ]);
        }

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

        buttons.Add([InlineKeyboardButton.WithCallbackData("Получить список покупок", $"shopping_list:{plan.Id}")]);

        return new InlineKeyboardMarkup(buttons);
    }

    private async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "Начинаю загрузку видео...", cancellationToken: cancellationToken);

        try
        {
            Recipe? recipe = await ExtractRecipeAsync(botClient, chatId, statusMessage.Id, url, cancellationToken);

            if (recipe == null)
            {
                await botClient.EditMessageText(chatId, statusMessage.Id, "Ошибка: Не удалось извлечь рецепт ни одним из способов.", cancellationToken: cancellationToken);
                return;
            }

            await _repository.SaveRecipeAsync(recipe);

            string formattedRecipe = FormatRecipeToMarkdown(recipe);

            await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(chatId, formattedRecipe, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при обработке {url}: {ex.Message}");
            await botClient.SendMessage(chatId, $"❌ Произошла ошибка при обработке: {ex.Message}", cancellationToken: cancellationToken);
        }
    }

    private async Task<Recipe?> ExtractRecipeAsync(ITelegramBotClient botClient, long chatId, int statusMessageId, string url, CancellationToken cancellationToken)
    {
        var metadata = await _downloader.DownloadAudioAsync(url);
        await botClient.EditMessageText(chatId, statusMessageId, $"⬇️ Видео успешно загружено: \"{metadata.Title}\". Пробую найти рецепт в описании...", cancellationToken: cancellationToken);

        Recipe? recipe = null;

        if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Length > 100)
            recipe = await _parser.ParseRecipeAsync(metadata.Description);

        if (IsRecipeMissing(recipe))
        {
            await botClient.EditMessageText(chatId, statusMessageId, "Рецепт в описании не найден. Проверяю закрепленный комментарий...", cancellationToken: cancellationToken);
            string? firstComment = await _downloader.GetFirstCommentAsync(url);

            if (!string.IsNullOrWhiteSpace(firstComment))
                recipe = await _parser.ParseRecipeAsync(firstComment);
        }

        if (IsRecipeMissing(recipe))
        {
            await botClient.EditMessageText(chatId, statusMessageId, "Текст не найден. Запускаю локальное распознавание речи (Whisper)...", cancellationToken: cancellationToken);

            string transcript = await _transcriber.TranscribeAsync(metadata.AudioFilePath);

            await botClient.EditMessageText(chatId, statusMessageId, "Распознавание завершено. Форматирую рецепт через ИИ...", cancellationToken: cancellationToken);
            recipe = await _parser.ParseRecipeAsync(transcript);
        }

        return recipe;
    }

    private bool IsRecipeMissing(Recipe? recipe)
    {
        return recipe == null ||
               recipe.Ingredients.Count == 0 ||
               recipe.Title == "Нет рецепта" ||
               recipe.Title == "Ошибка парсинга JSON";
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Ошибка Telegram API: {exception.Message}");
        return Task.CompletedTask;
    }

    private string FormatRecipeToMarkdown(Recipe recipe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {recipe.Title.ToUpper()}");
        sb.AppendLine();

        sb.AppendLine("### Ингредиенты:");
        
        foreach (var ing in recipe.Ingredients)
        {
            string amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"- {ing.Name}{amount}");
        }

        sb.AppendLine();
        sb.AppendLine("### Шаги приготовления:");
        
        foreach (var step in recipe.Steps.OrderBy(s => s.Number))
            sb.AppendLine($"{step.Number}. {step.Description}");

        return sb.ToString();
    }
}