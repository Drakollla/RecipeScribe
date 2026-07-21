using Shared.DTOs;

namespace TelegramBot.Contracts;

public interface IMealPlanApiClient
{
    Task<MealPlanDto?> GetPlanAsync(long chatId, DateOnly date, CancellationToken ct = default);
    Task<MealPlanDto> GeneratePlanAsync(long chatId, DateOnly date, string? preferences, CancellationToken ct = default);
    Task<string> GetShoppingListAsync(Guid planId, CancellationToken ct = default);
}
