using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.Hosting;
using RecipeScribe.Infrastructure.Database;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ConsoleApp;

public class TelegramBotService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IVideoDownloader _downloader;
    private readonly ITranscriber _transcriber;
    private readonly IRecipeParser _parser;
    private readonly RecipeRepository _repository;

    public TelegramBotService(ITelegramBotClient botClient,
        IVideoDownloader downloader,
        ITranscriber transcriber,
        IRecipeParser parser,
        RecipeRepository repository)
    {
        _botClient = botClient;
        _downloader = downloader;
        _transcriber = transcriber;
        _parser = parser;
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _botClient.GetMe(stoppingToken);
        Console.WriteLine($"Бот @{me.Username} успешно запущен как BackgroundService.");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
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
        if (update.Message is not { Text: { } messageText } message)
            return;

        long chatId = message.Chat.Id;
        string url = messageText.Trim();

        Console.WriteLine($"[Бот] Получено сообщение от {chatId}: {messageText}");

        if (url.StartsWith('/'))
        {
            await botClient.SendMessage(chatId, "Отправь мне ссылку на YouTube Shorts, TikTok или Pinterest с рецептом!", cancellationToken: cancellationToken);
            return;
        }

        bool isUrl = Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                     && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

        if (!isUrl)
        {
            await botClient.SendMessage(chatId, "Пожалуйста, отправь корректную ссылку.", cancellationToken: cancellationToken);
            return;
        }

        _ = Task.Run(() => ProcessVideoRecipeAsync(botClient, chatId, url, cancellationToken), cancellationToken);
    }

    private async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "⏳ Начинаю загрузку видео...", cancellationToken: cancellationToken);

        try
        {
            Recipe? recipe = await ExtractRecipeAsync(botClient, chatId, statusMessage.Id, url, cancellationToken);

            if (recipe == null)
            {
                await botClient.EditMessageText(chatId, statusMessage.Id, "❌ Ошибка: Не удалось извлечь рецепт ни одним из способов.", cancellationToken: cancellationToken);
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
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"*РЕЦЕПТ: {recipe.Title.ToUpper()}*");
        sb.AppendLine("=================================");
        sb.AppendLine();
        sb.AppendLine("*Ингредиенты:*");
        foreach (var ing in recipe.Ingredients)
        {
            string amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"  • {ing.Name}{amount}");
        }
        sb.AppendLine();
        sb.AppendLine("*Шаги приготовления:*");
        foreach (var step in recipe.Steps)
        {
            sb.AppendLine($"  {step.Number}. {step.Description}");
        }

        return sb.ToString();
    }
}