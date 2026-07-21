using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.DTOs;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class RecipeApiClient : IRecipeApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RecipeApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RecipeApiClient(HttpClient http, ILogger<RecipeApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<RecipeDto?> ExtractRecipeAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/recipes/extract", new CreateRecipeDto(url), ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RecipeDto>(cancellationToken: ct);
    }

    public async Task<List<RecipeDto>> SearchRecipesAsync(string ingredients, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"/api/recipes/search?ingredients={Uri.EscapeDataString(ingredients)}", ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, ingredients);
            return new();
        }

        return await response.Content.ReadFromJsonAsync<List<RecipeDto>>(ct) ?? new();
    }

    public async Task<RecipeDto?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/recipes/{id}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, id.ToString());
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RecipeDto>(cancellationToken: ct);
    }

    public async Task<List<RecipeSummaryDto>> GetAllRecipesAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/recipes", ct);

        if (!response.IsSuccessStatusCode)
        {
            await LogAndThrowAsync(response, "all");
            return new();
        }

        return await response.Content.ReadFromJsonAsync<List<RecipeSummaryDto>>(ct) ?? new();
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
