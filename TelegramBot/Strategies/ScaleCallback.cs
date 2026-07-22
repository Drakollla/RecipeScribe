using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class ScaleCallback : ICallbackQuery
{
    private const string Prefix = "scale:";

    public bool CanHandle(string data) => data.StartsWith(Prefix);

    public async Task ExecuteAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, UserStateInfo state, CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } message)
            return;

        var parts = callbackQuery.Data![Prefix.Length..].Split(':');
        if (parts.Length != 1 || !Guid.TryParse(parts[0], out var recipeId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Ошибка", cancellationToken: cancellationToken);
            return;
        }

        state.State = BotState.WaitingForServings;
        state.LastRecipeId = recipeId;

        await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        await botClient.SendMessage(message.Chat.Id,
            "На сколько порций изменить рецепт? Напишите число (1–20).",
            cancellationToken: cancellationToken);
    }
}
