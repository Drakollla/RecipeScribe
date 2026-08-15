using Core.Enums;
using Core.Models;

namespace Core.Contracts;

public interface IMealPlanRepository
{
    Task<User> GetOrCreateUserAsync(long telegramChatId);
    Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date);
    Task<MealPlan> CreatePlanAsync(MealPlan plan);
    Task<Recipe?> GetRecipeByMealTypeAsync(MealType mealType, List<Guid> excludeIds);
    Task<Recipe?> GetRecipeByIdAsync(Guid recipeId);
    Task UpdateRecipeLastPlannedAtAsync(Guid recipeId);
    Task<List<MealPlanItem>> GetPlanItemsWithRecipesAsync(Guid mealPlanId);
    Task<MealPlanItem?> GetPlanItemByIdAsync(Guid planItemId);
    Task<MealPlanItem?> UpdatePlanItemAsync(Guid planItemId, int portions, Guid? recipeId = null);
    Task UpdateUserAsync(long telegramChatId, int defaultServings, string? obsidianVaultPath = null);
}
