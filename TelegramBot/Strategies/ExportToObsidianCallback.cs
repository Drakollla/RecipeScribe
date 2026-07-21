using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBot.Contracts;

namespace TelegramBot.Strategies;

public class ExportToObsidianCallback : ICallbackQuery
{
    private const string Prefix = "export_obsidian:";

    private readonly IExportApiClient _exportApi;
    private readonly ILogger<ExportToObsidianCallback> _logger;

    public ExportToObsidianCallback(IExportApiClient exportApi, ILogger<ExportToObsidianCallback> logger)
    {
        _exportApi = exportApi;
        _logger = logger;
    }

    public bool CanHandle(string data) => data.StartsWith(Prefix);

    public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } message)
            return;

        var recipeId = Guid.Parse(callbackQuery.Data![Prefix.Length..]);

        try
        {
            var success = await _exportApi.ExportToObsidianAsync(recipeId, cancellationToken);

            await botClient.AnswerCallbackQuery(callbackQuery.Id,
                success ? "Рецепт сохранён в Obsidian" : "Не удалось сохранить. Путь не настроен или ошибка.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export to Obsidian failed for {Id}", recipeId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Ошибка экспорта", cancellationToken: cancellationToken);
        }
    }
}
