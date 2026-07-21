using Shared.DTOs;
using System.Net.Http.Json;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class MealPlanApiClient : IMealPlanApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MealPlanApiClient> _logger;

    public MealPlanApiClient(HttpClient http, ILogger<MealPlanApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<MealPlanDto?> GetPlanAsync(long chatId, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MealPlanDto>(
                $"/api/mealplans?chatId={chatId}&date={date:yyyy-MM-dd}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting meal plan for {ChatId} on {Date}", chatId, date);
            return null;
        }
    }

    public async Task<MealPlanDto> GeneratePlanAsync(long chatId, DateOnly date, string? preferences, CancellationToken ct = default)
    {
        var request = new CreateMealPlanDto(date.ToString("yyyy-MM-dd"), preferences);
        var response = await _http.PostAsJsonAsync($"/api/mealplans/generate?chatId={chatId}", request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MealPlanDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("API returned null for meal plan generation");
    }

    public async Task<string> GetShoppingListAsync(Guid planId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/mealplans/{planId}/shopping-list", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }
}
