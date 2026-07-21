using Shared.DTOs;
using System.Net.Http.Json;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class SubstitutionApiClient : ISubstitutionApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SubstitutionApiClient> _logger;

    public SubstitutionApiClient(HttpClient http, ILogger<SubstitutionApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<SubstitutionDto?> GetSubstitutionAsync(string ingredient, string recipeTitle, CancellationToken ct = default)
    {
        try
        {
            var request = new CreateSubstitutionDto(ingredient, recipeTitle);
            var response = await _http.PostAsJsonAsync("/api/substitutions", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SubstitutionDto>(cancellationToken: ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting substitution for {Ingredient} in {Recipe}", ingredient, recipeTitle);
            return null;
        }
    }
}
