using Core.Enums;
using Core.Models;

namespace Core.Contracts;

public interface IMealPlanRepository
{
    Task<User> GetOrCreateUserAsync(long telegramChatId);
    Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date);
    Task<MealPlan> CreatePlanAsync(MealPlan plan);
    Task<Recipe?> GetRecipeByMealTypeAsync(MealType mealType, List<Guid> excludeIds);
    Task UpdateRecipeLastPlannedAtAsync(Guid recipeId);
    Task<List<MealPlanItem>> GetPlanItemsWithRecipesAsync(Guid mealPlanId);
    Task UpdateUserAsync(long telegramChatId, int defaultServings, string? obsidianVaultPath = null);
}
