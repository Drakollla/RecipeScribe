using Core.Contracts;
using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class SubstituteCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state) =>
        text.StartsWith("/substitute", StringComparison.OrdinalIgnoreCase);

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        state.State = BotState.WaitingForSubstituteIngredient;
        state.LastSubstituteIngredient = null;

        await botClient.SendMessage(message.Chat.Id,
            "Напишите, какой ингредиент вы хотите заменить (например: курица):",
            cancellationToken: cancellationToken);
    }
}
