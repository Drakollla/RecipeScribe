using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.DTOs;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class MealPlanApiClient : IMealPlanApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MealPlanApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MealPlanApiClient(HttpClient http, ILogger<MealPlanApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<MealPlanDto?> GetPlanAsync(long chatId, DateOnly date, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/mealplans?chatId={chatId}&date={date:yyyy-MM-dd}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, $"{chatId}/{date}");
            return null;
        }

        return await response.Content.ReadFromJsonAsync<MealPlanDto>(cancellationToken: ct);
    }

    public async Task<MealPlanDto> GeneratePlanAsync(long chatId, DateOnly date, string? preferences, CancellationToken ct = default)
    {
        var request = new CreateMealPlanDto(date.ToString("yyyy-MM-dd"), preferences);
        var response = await _http.PostAsJsonAsync($"/api/mealplans/generate?chatId={chatId}", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, $"{chatId}/{date}");
            return null!;
        }

        return await response.Content.ReadFromJsonAsync<MealPlanDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("API returned null for meal plan generation");
    }

    public async Task<string> GetShoppingListAsync(Guid planId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/mealplans/{planId}/shopping-list", ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, planId.ToString());
            return "";
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task LogAndThrowAsync(HttpResponseMessage response, string context = "")
    {
        var body = await response.Content.ReadAsStringAsync();
        ErrorDto? error = null;
        try { error = JsonSerializer.Deserialize<ErrorDto>(body, JsonOptions); } catch { }

        var errorType = error?.ErrorType ?? "Unknown";
        var message = error?.Error ?? body;

        _logger.LogError("API error [{StatusCode}] {ErrorType} for {Context}: {Message}",
            (int)response.StatusCode, errorType, context, message);

        throw new HttpRequestException(
            $"{errorType}: {message}",
            null,
            response.StatusCode);
    }
}
