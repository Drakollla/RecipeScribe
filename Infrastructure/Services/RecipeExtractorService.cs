using Core.Contracts;
using Core.Models;

namespace Infrastructure.Services
{
    public class RecipeExtractorService : IRecipeExtractorService
    {
        private readonly IVideoDownloader _downloader;
        private readonly ITranscriber _transcriber;
        private readonly IRecipeParser _parser;
        private readonly IRecipeRepository _repository;

        public RecipeExtractorService(
            IVideoDownloader downloader,
            ITranscriber transcriber,
            IRecipeParser parser,
            IRecipeRepository repository)
        {
            _downloader = downloader;
            _transcriber = transcriber;
            _parser = parser;
            _repository = repository;
        }
        public async Task<Recipe?> ExtractAndSaveRecipeAsync(string url, Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
        {
            var existingRecipe = await _repository.GetRecipeByUrlAsync(url);

            if (existingRecipe != null)
            {
                if (onProgress != null)
                    await onProgress("Рецепт найден в локальной базе данных! Загружаю...");

                return existingRecipe;
            }

            var metadata = await _downloader.DownloadAudioAsync(url);

            Recipe? recipe = null;

            if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Length > 100)
            {
                if (onProgress != null)
                    await onProgress("⬇️ Видео загружено. Пробую найти рецепт в описании...");

                recipe = await _parser.ParseRecipeAsync(metadata.Description);
            }

            if (IsRecipeMissing(recipe))
            {
                if (onProgress != null)
                    await onProgress("Рецепт в описании не найден. Проверяю закрепленный комментарий...");

                string? firstComment = await _downloader.GetFirstCommentAsync(url);

                if (!string.IsNullOrWhiteSpace(firstComment))
                    recipe = await _parser.ParseRecipeAsync(firstComment);
            }

            if (IsRecipeMissing(recipe))
            {
                if (onProgress != null)
                    await onProgress("Текст не найден. Запускаю локальное распознавание речи (Whisper)...");

                string transcript = await _transcriber.TranscribeAsync(metadata.AudioFilePath);

                if (onProgress != null)
                    await onProgress("Распознавание завершено. Форматирую рецепт через ИИ...");

                recipe = await _parser.ParseRecipeAsync(transcript);
            }

            if (recipe != null)
            {
                if (string.IsNullOrWhiteSpace(recipe.Title) || recipe.Title.Trim() == "#" || recipe.Title == "Нет рецепта")
                    recipe.Title = CleanVideoTitle(metadata.Title);

                await _repository.SaveRecipeAsync(recipe);
            }

            return recipe;
        }

        private bool IsRecipeMissing(Recipe? recipe)
        {
            return recipe == null ||
                   recipe.Ingredients.Count == 0 ||
                   recipe.Title == "Нет рецепта" ||
                   recipe.Title == "Ошибка парсинга JSON";
        }

        private string CleanVideoTitle(string title)
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
