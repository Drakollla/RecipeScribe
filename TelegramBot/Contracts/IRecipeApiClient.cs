using Shared.DTOs;

namespace TelegramBot.Contracts;

public interface IRecipeApiClient
{
    Task<RecipeDto?> ExtractRecipeAsync(string url, CancellationToken ct = default);
    Task<List<RecipeDto>> SearchRecipesAsync(string ingredients, CancellationToken ct = default);
    Task<RecipeDto?> GetRecipeByIdAsync(Guid id, int? servings = null, CancellationToken ct = default);
    Task<List<RecipeSummaryDto>> GetAllRecipesAsync(CancellationToken ct = default);
}
