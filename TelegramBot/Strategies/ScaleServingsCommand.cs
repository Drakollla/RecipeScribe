using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Contracts;

namespace TelegramBot.Strategies;

public class ScaleServingsCommand : IMessageCommand
{
    private readonly IRecipeApiClient _recipeApi;
    private readonly TelegramRecipeFlow _recipeFlow;
    private readonly ILogger<ScaleServingsCommand> _logger;

    public ScaleServingsCommand(IRecipeApiClient recipeApi, TelegramRecipeFlow recipeFlow, ILogger<ScaleServingsCommand> logger)
    {
        _recipeApi = recipeApi;
        _recipeFlow = recipeFlow;
        _logger = logger;
    }

    public bool CanHandle(string text, BotState state) =>
        state == BotState.WaitingForServings;

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        var input = message.Text?.Trim();

        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out var servings) || servings < 1 || servings > 20)
        {
            await botClient.SendMessage(message.Chat.Id,
                "Пожалуйста, напишите число от 1 до 20.",
                cancellationToken: cancellationToken);
            return;
        }

        if (state.LastRecipeId is not { } recipeId)
        {
            state.State = BotState.None;
            await botClient.SendMessage(message.Chat.Id, "Ошибка: рецепт не найден.", cancellationToken: cancellationToken);
            return;
        }

        state.State = BotState.None;
        state.LastRecipeId = null;

        try
        {
            var recipe = await _recipeApi.GetRecipeByIdAsync(recipeId, servings, cancellationToken);

            if (recipe == null)
            {
                await botClient.SendMessage(message.Chat.Id, "Рецепт не найден.", cancellationToken: cancellationToken);
                return;
            }

            var caption = $"<b>Рецепт:</b> <i>{HtmlHelper.Escape(recipe.Title)}</i>\n<b>Порции:</b> {servings}";
            var keyboard = ScaleCommandKeyboard(recipeId, servings);

            var (stream, fileName) = _recipeFlow.BuildRecipeDocument(recipe);
            await using (stream.ConfigureAwait(false))
                await botClient.SendDocument(
                    chatId: message.Chat.Id,
                    document: InputFile.FromStream(stream, fileName),
                    caption: caption,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scale failed for {Id} to {Servings}", recipeId, servings);
            await botClient.SendMessage(message.Chat.Id, "Ошибка при изменении порций.", cancellationToken: cancellationToken);
        }
    }

    internal static InlineKeyboardMarkup ScaleCommandKeyboard(Guid recipeId, int servings)
    {
        return new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("🍽 Изменить порции", $"scale:{recipeId}")]
        ]);
    }
}
