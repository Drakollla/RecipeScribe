using Core.Contracts;
using Core.Helpers;
using Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Infrastructure.Services;

public class RecipeExtractorService : IRecipeExtractorService
{
    private readonly IVideoDownloader _downloader;
    private readonly ITranscriber _transcriber;
    private readonly IRecipeParser _parser;
    private readonly IRecipeRepository _repository;
    private readonly ILogger<RecipeExtractorService> _logger;

    public RecipeExtractorService(
        IVideoDownloader downloader,
        ITranscriber transcriber,
        IRecipeParser parser,
        IRecipeRepository repository,
        ILogger<RecipeExtractorService> logger)
    {
        _downloader = downloader;
        _transcriber = transcriber;
        _parser = parser;
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<Recipe>> ExtractAndSaveRecipeAsync(string url, Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var existingRecipes = await _repository.GetRecipesByUrlAsync(url);

        if (existingRecipes.Count > 0)
        {
            if (onProgress != null)
                await onProgress("Рецепты найдены в локальной базе данных! Загружаю...");

            return existingRecipes;
        }

        var metadata = await _downloader.DownloadAudioAsync(url, cancellationToken);
        List<Recipe> recipes = new();

        if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Length > 100)
        {
            if (onProgress != null)
                await onProgress("Видео загружено. Пробую найти рецепт в описании...");

            recipes = await TryParseRecipesAsync(metadata.Description, cancellationToken);
        }

        if (recipes.Count == 0)
        {
            if (onProgress != null)
                await onProgress("Рецепт в описании не найден. Проверяю закрепленный комментарий...");

            string? firstComment = await _downloader.GetFirstCommentAsync(url, cancellationToken);

            if (!string.IsNullOrWhiteSpace(firstComment))
                recipes = await TryParseRecipesAsync(firstComment, cancellationToken);
        }

        if (recipes.Count == 0)
        {
            string transcript = await GetOrCreateTranscriptAsync(metadata, onProgress, cancellationToken);

            if (onProgress != null)
                await onProgress("Распознавание завершено. Форматирую рецепты через ИИ...");

            recipes = await TryParseRecipesAsync(transcript, cancellationToken);
        }

        foreach (var recipe in recipes)
            await SaveRecipeAsync(recipe, url, metadata.Title);

        return recipes;
    }

    private async Task<string> GetOrCreateTranscriptAsync(ViewMetadata metadata, Func<string, Task>? onProgress, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(metadata.CachedTranscript))
        {
            if (onProgress != null)
                await onProgress("Использую ранее распознанную речь из локального кэша...");

            return metadata.CachedTranscript;
        }

        if (onProgress != null)
            await onProgress("Текст не найден. Запускаю локальное распознавание речи (Whisper)...");

            string transcript = await _transcriber.TranscribeAsync(metadata.AudioFilePath, ct);

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

    private async Task<List<Recipe>> TryParseRecipesAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var recipes = await LlmRetryHelper.CallWithRetryAsync(
                () => _parser.ParseRecipesAsync(text, cancellationToken),
                validateResult: list => list.Any(r => !IsRecipeMissing(r)),
                logger: _logger,
                logPrefix: "ИИ",
                ct: cancellationToken);

            return recipes.Where(r => !IsRecipeMissing(r)).ToList();
        }
        catch
        {
            return new List<Recipe>();
        }
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