using Core.Contracts;
using Core.Helpers;
using Core.Models;
using System.Text;

namespace Infrastructure.Services;

public class RecipeExtractorService : IRecipeExtractorService
{
    const int maxRetries = 5;

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
                await onProgress("Видео загружено. Пробую найти рецепт в описании...");

            recipe = await ParseRecipeAsync(metadata.Description, cancellationToken);
        }

        if (IsRecipeMissing(recipe))
        {
            if (onProgress != null)
                await onProgress("Рецепт в описании не найден. Проверяю закрепленный комментарий...");

            string? firstComment = await _downloader.GetFirstCommentAsync(url);

            if (!string.IsNullOrWhiteSpace(firstComment))
                recipe = await ParseRecipeAsync(firstComment, cancellationToken);
        }

        if (IsRecipeMissing(recipe))
        {
            string transcript = await GetOrCreateTranscriptAsync(metadata, onProgress);

            if (onProgress != null)
                await onProgress("Распознавание завершено. Форматирую рецепт через ИИ...");

            recipe = await ParseRecipeAsync(transcript, cancellationToken);
        }

        if (recipe != null)
            await SaveRecipeAsync(recipe, url, metadata.Title);

        return recipe;
    }

    private async Task<string> GetOrCreateTranscriptAsync(ViewMetadata metadata, Func<string, Task>? onProgress)
    {
        if (!string.IsNullOrEmpty(metadata.CachedTranscript))
        {
            if (onProgress != null)
                await onProgress("Использую ранее распознанную речь из локального кэша...");

            return metadata.CachedTranscript;
        }

        if (onProgress != null)
            await onProgress("Текст не найден. Запускаю локальное распознавание речи (Whisper)...");

        string transcript = await _transcriber.TranscribeAsync(metadata.AudioFilePath);

        string directory = Path.GetDirectoryName(metadata.AudioFilePath)!;
        string fileName = Path.GetFileNameWithoutExtension(metadata.AudioFilePath);
        string transcriptPath = Path.Combine(directory, $"{fileName}.txt");

        await File.WriteAllTextAsync(transcriptPath, transcript, Encoding.UTF8);

        if (File.Exists(metadata.AudioFilePath))
            File.Delete(metadata.AudioFilePath);

        return transcript;
    }

    private async Task SaveRecipeAsync(Recipe recipe, string url, string videoTitle)
    {
        recipe.VideoUrl = url.Trim();

        if (string.IsNullOrWhiteSpace(recipe.Title) || recipe.Title.Trim() == "#" || recipe.Title == "Нет рецепта")
            recipe.Title = CleanVideoTitle(videoTitle);

        await _repository.SaveRecipeAsync(recipe);
    }

    private async Task<Recipe?> ParseRecipeAsync(string text, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var recipe = await _parser.ParseRecipeAsync(text);

                if (recipe != null && !IsRecipeMissing(recipe))
                    return recipe;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ИИ] Попытка {attempt}/{maxRetries} завершилась ошибкой парсинга JSON: {ex.Message}");

                if (attempt == maxRetries)
                    throw;

                await Task.Delay(1000, cancellationToken);
            }
        }
        return null;
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