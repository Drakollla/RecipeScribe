using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBot.Contracts;

namespace TelegramBot.Strategies;

public class SubstituteRecipeCommand : IMessageCommand
{
    private readonly ISubstitutionApiClient _substitutionApi;
    private readonly ILogger<SubstituteRecipeCommand> _logger;

    public SubstituteRecipeCommand(ISubstitutionApiClient substitutionApi, ILogger<SubstituteRecipeCommand> logger)
    {
        _substitutionApi = substitutionApi;
        _logger = logger;
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

        try
        {
            var result = await _substitutionApi.GetSubstitutionAsync(ingredient, recipeTitle, cancellationToken);

            if (result == null)
            {
                await botClient.DeleteMessage(message.Chat.Id, statusMessage.Id, cancellationToken: cancellationToken);
                await botClient.SendMessage(message.Chat.Id,
                    "Не удалось найти замену для указанного ингредиента.",
                    cancellationToken: cancellationToken);
                return;
            }

            await botClient.DeleteMessage(message.Chat.Id, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(message.Chat.Id,
                $"<b>Замена для \"{ingredient}\" в рецепте \"{recipeTitle}\":</b>\n\n{HtmlHelper.Escape(result.Result)}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API error getting substitution");
            await botClient.DeleteMessage(message.Chat.Id, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(message.Chat.Id,
                $"Ошибка при поиске замены: {ex.Message}",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при замене ингредиента");
            await botClient.DeleteMessage(message.Chat.Id, statusMessage.Id, cancellationToken: cancellationToken);
            await botClient.SendMessage(message.Chat.Id,
                "Произошла ошибка при поиске замены.",
                cancellationToken: cancellationToken);
        }
    }
}
