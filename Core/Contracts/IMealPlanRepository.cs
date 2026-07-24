using Core.Models;
using System.Linq.Expressions;

namespace Core.Contracts;

public interface IMealPlanRepository
{
    Task<User> GetOrCreateUserAsync(long telegramChatId);
    Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date);
    Task<MealPlan> CreatePlanAsync(MealPlan plan);
    Task<List<Guid>> GetRecentRecipeIdsAsync(Guid userId, DateOnly date);
    Task<List<RecipeCandidate>> GetCategoryCandidatesAsync(
        Expression<Func<Recipe, bool>> categoryPredicate,
        List<Guid> excludeIds, string? primaryKeyword);
    Task<List<MealPlanItem>> GetPlanItemsWithRecipesAsync(Guid mealPlanId);
    Task UpdateUserAsync(long telegramChatId, int defaultServings, string? obsidianVaultPath = null);
}
