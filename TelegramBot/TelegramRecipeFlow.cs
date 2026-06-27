using Core.Contracts;
using Core.Models;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
    public class TelegramRecipeFlow
    {
        private readonly IVideoDownloader _downloader;
        private readonly ITranscriber _transcriber;
        private readonly IRecipeParser _parser;
        private readonly IRecipeRepository _repository;

        public TelegramRecipeFlow(IVideoDownloader downloader,
            ITranscriber transcriber,
            IRecipeParser parser,
            IRecipeRepository repository)
        {
            _downloader = downloader;
            _transcriber = transcriber;
            _parser = parser;
            _repository = repository;
        }

        public async Task ProcessVideoRecipeAsync(ITelegramBotClient botClient, long chatId, string url, CancellationToken cancellationToken)
        {
            var statusMessage = await botClient.SendMessage(chatId, "Начинаю загрузку видео...", cancellationToken: cancellationToken);

            try
            {
                Recipe? recipe = await ExtractRecipeAsync(botClient, chatId, statusMessage.Id, url, cancellationToken);

                if (recipe == null)
                {
                    await botClient.EditMessageText(chatId, statusMessage.Id, "Ошибка: Не удалось извлечь рецепт ни одним из способов.", cancellationToken: cancellationToken);
                    return;
                }

                await _repository.SaveRecipeAsync(recipe);

                string formattedRecipe = FormatRecipeToMarkdown(recipe);

                await botClient.DeleteMessage(chatId, statusMessage.Id, cancellationToken: cancellationToken);
                await botClient.SendMessage(chatId, formattedRecipe, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке {url}: {ex.Message}");
                await botClient.SendMessage(chatId, $"Произошла ошибка при обработке: {ex.Message}", cancellationToken: cancellationToken);
            }
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
                caption: $"📖 *Рецепт:* _{recipe.Title}_",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
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

            if (recipe != null && (string.IsNullOrWhiteSpace(recipe.Title) || recipe.Title.Trim() == "#" || recipe.Title == "Нет рецепта"))
                recipe.Title = GetCleanVideoTitle(metadata.Title);

            return recipe;
        }


        public async Task ProcessSearchByIngredientsAsync(ITelegramBotClient botClient, long chatId, string inputText, CancellationToken cancellationToken)
        {
            var products = inputText.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (!products.Any())
            {
                await botClient.SendMessage(chatId, TelegramUiElements.SearchEmptyError, cancellationToken: cancellationToken);
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

        private bool IsRecipeMissing(Recipe? recipe)
        {
            return recipe == null ||
                   recipe.Ingredients.Count == 0 ||
                   recipe.Title == "Нет рецепта" ||
                   recipe.Title == "Ошибка парсинга JSON";
        }

        public string FormatRecipeToMarkdown(Recipe recipe)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {recipe.Title.ToUpper()}");
            sb.AppendLine();

            sb.AppendLine("### Ингредиенты:");

            foreach (var ing in recipe.Ingredients)
            {
                string amount = string.IsNullOrWhiteSpace(ing.Amount) ? "" : $" — {ing.Amount}";
                sb.AppendLine($"- {ing.Name}{amount}");
            }

            sb.AppendLine();
            sb.AppendLine("### Шаги приготовления:");

            foreach (var step in recipe.Steps.OrderBy(s => s.Number))
                sb.AppendLine($"{step.Number}. {step.Description}");

            return sb.ToString();
        }

        private string GetCleanVideoTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "Рецепт";

            var words = title.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanWords = words.Where(w => !w.StartsWith("#"));
            var cleanTitle = string.Join(" ", cleanWords).Trim();

            return string.IsNullOrWhiteSpace(cleanTitle) ? "Рецепт" : cleanTitle;
        }
    }
}
