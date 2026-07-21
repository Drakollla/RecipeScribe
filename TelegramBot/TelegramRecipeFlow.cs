using Microsoft.Extensions.Logging;
using Shared.DTOs;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Contracts;

namespace TelegramBot;

public class TelegramRecipeFlow
{
    private readonly IRecipeApiClient _recipeApi;
    private readonly ILogger<TelegramRecipeFlow> _logger;

    public TelegramRecipeFlow(IRecipeApiClient recipeApi, ILogger<TelegramRecipeFlow> logger)
    {
        _recipeApi = recipeApi;
        _logger = logger;
    }

    public async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
    {
        var statusMessage = await botClient.SendMessage(chatId, "Извлекаю рецепт из видео...", cancellationToken: cancellationToken);

        try
        {
            await botClient.EditMessageText(chatId, statusMessage.Id, "Обрабатываю видео...", cancellationToken: cancellationToken);

            var recipe = await _recipeApi.ExtractRecipeAsync(url, cancellationToken);

            if (recipe == null)
            {
                await botClient.EditMessageText(chatId, statusMessage.Id, "Ошибка: не удалось извлечь рецепт.", cancellationToken: cancellationToken);
                return;
            }

            string formattedRecipe = FormatRecipeToMarkdown(recipe);
            byte[] mdBytes = Encoding.UTF8.GetBytes(formattedRecipe);
            using var stream = new MemoryStream(mdBytes);
            string fileName = $"{recipe.Title.Replace(" ", "_")}.md";

            await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);

            await botClient.SendDocument(
                chatId: chatId,
                document: InputFile.FromStream(stream, fileName),
                caption: $"*Рецепт:* _{recipe.Title}_",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении {Url}", url);
            await botClient.SendMessage(chatId, "Произошла ошибка при извлечении. Попробуйте другой URL или повторите позже.", cancellationToken: cancellationToken);
        }
    }

    public async Task ProcessSearchByIngredientsAsync(ITelegramBotClient botClient, long chatId, string inputText, CancellationToken cancellationToken)
    {
        var matchingRecipes = await _recipeApi.SearchRecipesAsync(inputText, cancellationToken);

        if (matchingRecipes.Count == 0)
        {
            await botClient.SendMessage(chatId, "Подходящих рецептов в базе не найдено.", cancellationToken: cancellationToken);
            return;
        }

        var buttons = new List<List<InlineKeyboardButton>>();
        foreach (var recipe in matchingRecipes)
            buttons.Add([InlineKeyboardButton.WithCallbackData(recipe.Title, $"show_recipe:{recipe.Id}")]);

        var keyboard = new InlineKeyboardMarkup(buttons);

        await botClient.SendMessage(
            chatId: chatId,
            text: $"Найдено подходящих рецептов: {matchingRecipes.Count}. Выберите один для просмотра:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken
        );
    }

    public async Task SendRecipeDocumentAsync(ITelegramBotClient botClient, long chatId, Guid recipeId, CancellationToken cancellationToken)
    {
        var recipe = await _recipeApi.GetRecipeByIdAsync(recipeId, cancellationToken);

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
            caption: $"*Рецепт:* _{MarkdownHelper.Escape(recipe.Title)}_",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: cancellationToken
        );
    }

    public string FormatRecipeToMarkdown(RecipeDto recipe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {MarkdownHelper.Escape(recipe.Title.ToUpper())}");
        sb.AppendLine();

        sb.AppendLine("### Ингредиенты:");

        foreach (var ing in recipe.Ingredients)
        {
            string amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
            sb.AppendLine($"- {MarkdownHelper.Escape(ing.Name)}{amount}");
        }

        if (recipe.PreparationTips is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### Советы по подготовке:");

            foreach (var tip in recipe.PreparationTips)
                sb.AppendLine($"- **{MarkdownHelper.Escape(tip.Ingredient)}:** {MarkdownHelper.Escape(tip.Tip)}");
        }

        sb.AppendLine();
        sb.AppendLine("### Шаги приготовления:");

        foreach (var step in recipe.Steps.OrderBy(s => s.Number))
            sb.AppendLine($"{step.Number}. {MarkdownHelper.Escape(step.Description)}");

        return sb.ToString();
    }
}
