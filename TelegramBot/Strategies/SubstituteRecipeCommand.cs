using Core.Contracts;
using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies;

public class SubstituteRecipeCommand : IMessageCommand
{
    private readonly ISubstitutionService _substitutionService;

    public SubstituteRecipeCommand(ISubstitutionService substitutionService)
    {
        _substitutionService = substitutionService;
    }

    public bool CanHandle(string text, BotState state) =>
        state == BotState.WaitingForSubstituteRecipe;

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        var recipeTitle = message.Text?.Trim();

        if (string.IsNullOrWhiteSpace(recipeTitle))
        {
            await botClient.SendMessage(message.Chat.Id,
                "Название рецепта не может быть пустым. Напишите название блюда:",
                cancellationToken: cancellationToken);
            return;
        }

        var ingredient = state.LastSubstituteIngredient ?? string.Empty;
        state.State = BotState.None;
        state.LastSubstituteIngredient = null;

        var statusMessage = await botClient.SendMessage(message.Chat.Id, "Ищу варианты замены...", cancellationToken: cancellationToken);

        var result = await _substitutionService.GetSubstitutionsAsync(ingredient, recipeTitle, cancellationToken);

        await botClient.DeleteMessage(message.Chat.Id, statusMessage.Id, cancellationToken: cancellationToken);
        await botClient.SendMessage(message.Chat.Id,
            $"*Замена для \"{ingredient}\" в рецепте \"{recipeTitle}\":*\n\n{result}",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }
}
