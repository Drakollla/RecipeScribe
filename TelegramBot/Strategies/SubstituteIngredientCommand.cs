using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class SubstituteIngredientCommand : IMessageCommand
{
    public bool CanHandle(string text, BotState state) =>
        state == BotState.WaitingForSubstituteIngredient;

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        var ingredient = message.Text?.Trim();

        if (string.IsNullOrWhiteSpace(ingredient))
        {
            await botClient.SendMessage(message.Chat.Id,
                "Ингредиент не может быть пустым. Напишите название ингредиента:",
                cancellationToken: cancellationToken);
            return;
        }

        state.LastSubstituteIngredient = ingredient;
        state.State = BotState.WaitingForSubstituteRecipe;

        await botClient.SendMessage(message.Chat.Id,
            "В каком рецепте? Напишите название блюда (например: Салат с курицей и ананасами):",
            cancellationToken: cancellationToken);
    }
}
