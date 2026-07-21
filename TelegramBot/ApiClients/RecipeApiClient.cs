using Shared.DTOs;
using System.Net.Http.Json;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class RecipeApiClient : IRecipeApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RecipeApiClient> _logger;

    public RecipeApiClient(HttpClient http, ILogger<RecipeApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<RecipeDto?> ExtractRecipeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/recipes/extract", new { url }, ct);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<RecipeDto>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting recipe from {Url}", url);
            return null;
        }
    }

    public async Task<List<RecipeDto>> SearchRecipesAsync(string ingredients, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<RecipeDto>>(
                $"/api/recipes/search?ingredients={Uri.EscapeDataString(ingredients)}", ct) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching recipes by {Ingredients}", ingredients);
            return new();
        }
    }

    public async Task<RecipeDto?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<RecipeDto>($"/api/recipes/{id}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recipe {Id}", id);
            return null;
        }
    }

    public async Task<List<RecipeSummaryDto>> GetAllRecipesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<RecipeSummaryDto>>("/api/recipes", ct) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all recipes");
            return new();
        }
    }
}
