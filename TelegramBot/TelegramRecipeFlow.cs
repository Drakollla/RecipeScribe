using Core.Contracts;
using Core.Exceptions;
using Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
    public class TelegramRecipeFlow
    {
        private readonly IRecipeExtractorService _recipeExtractor;
        private readonly IRecipeRepository _repository;
        private readonly ILogger<TelegramRecipeFlow> _logger;

        public TelegramRecipeFlow(
            IRecipeExtractorService recipeExtractor,
            IRecipeRepository repository,
            ILogger<TelegramRecipeFlow> logger)
        {
            _recipeExtractor = recipeExtractor;
            _repository = repository;
            _logger = logger;
        }

        public async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
        {
            var statusMessage = await botClient.SendMessage(chatId, "Начинаю загрузку видео...", cancellationToken: cancellationToken);

            try
            {
                Recipe? recipe = await _recipeExtractor.ExtractAndSaveRecipeAsync(url, async status =>
                {
                    await botClient.EditMessageText(chatId, statusMessage.Id, status, cancellationToken: cancellationToken);
                }, cancellationToken);

                if (recipe == null)
                {
                    await botClient.EditMessageText(chatId, statusMessage.Id, "Ошибка: Не удалось извлечь рецепт ни одним из способов.", cancellationToken: cancellationToken);
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
                    caption: $"📖 *Рецепт:* _{recipe.Title}_",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
            catch (RecipeScribeException ex)
            {
                _logger.LogError(ex, "Ошибка при обработке {Url}", url);
                await botClient.SendMessage(chatId, $"❌ {ex.Message}", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке {Url}", url);
                await botClient.SendMessage(chatId, "❌ Неизвестная ошибка при обработке. Попробуйте другой URL или повторите позже.", cancellationToken: cancellationToken);
            }
        }

        public async Task ProcessSearchByIngredientsAsync(ITelegramBotClient botClient, long chatId, string inputText, CancellationToken cancellationToken)
        {
            var products = inputText.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (!products.Any())
            {
                await botClient.SendMessage(chatId, "Ошибка: Список ингредиентов пуст.", cancellationToken: cancellationToken);
                return;
            }

            var matchingRecipes = await _repository.SearchByIngredientsAsync(products);

            if (!matchingRecipes.Any())
            {
                await botClient.SendMessage(chatId, "Подходящих рецептов в базе данных не найдено.", cancellationToken: cancellationToken);
                return;
            }

            var buttons = new List<List<InlineKeyboardButton>>();
            foreach (var recipe in matchingRecipes)
                buttons.Add([InlineKeyboardButton.WithCallbackData(recipe.Title, $"show_recipe:{recipe.Id}")]);

            var keyboard = new InlineKeyboardMarkup(buttons);

            await botClient.SendMessage(
                chatId: chatId,
                text: $"Найдено подходящих рецептов: {matchingRecipes.Count}. Выберите рецепт для просмотра:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken
            );
        }

        public async Task SendRecipeDocumentAsync(ITelegramBotClient botClient, long chatId, Guid recipeId, CancellationToken cancellationToken)
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
                caption: $"📖 *Рецепт:* _{MarkdownHelper.Escape(recipe.Title)}_",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }

        public string FormatRecipeToMarkdown(Recipe recipe)
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

            if (!string.IsNullOrWhiteSpace(recipe.PreparationTips))
            {
                sb.AppendLine();
                sb.AppendLine("### Подготовка ингредиентов:");

                try
                {
                    var tips = JsonSerializer.Deserialize<List<PreparationTip>>(recipe.PreparationTips);

                    if (tips != null)
                    {
                        foreach (var tip in tips)
                            sb.AppendLine($"- **{MarkdownHelper.Escape(tip.Ingredient)}:** {MarkdownHelper.Escape(tip.Tip)}");
                    }
                }
                catch
                {
                    sb.AppendLine("_Не удалось отобразить советы по подготовке._");
                }
            }

            sb.AppendLine();
            sb.AppendLine("### Шаги приготовления:");

            foreach (var step in recipe.Steps.OrderBy(s => s.Number))
                sb.AppendLine($"{step.Number}. {MarkdownHelper.Escape(step.Description)}");

            return sb.ToString();
        }
    }
}