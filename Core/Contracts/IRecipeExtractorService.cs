using Core.Models;

namespace Core.Contracts;

public interface IRecipeExtractorService
{
    Task<List<Recipe>> ExtractAndSaveRecipeAsync(string url,
        Func<string, Task>? onProgress = null,
        CancellationToken cancellationToken = default);
}