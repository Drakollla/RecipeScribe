using Core.Enums;
using Core.Models;

namespace Core.Contracts
{
    public interface IMealPlannerService
    {
        Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date);

        Task<MealPlan> CreatePlanManualAsync(long telegramChatId, DateOnly date, Dictionary<MealType, Guid> mealRecipes);

        Task<MealPlan> GenerateSmartPlanAsync(long telegramChatId, DateOnly date, string userRequest);

        Task<string> GetShoppingListAsync(Guid mealPlanId);
    }
}