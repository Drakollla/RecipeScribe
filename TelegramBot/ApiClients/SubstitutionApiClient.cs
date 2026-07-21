using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.DTOs;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class SubstitutionApiClient : ISubstitutionApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SubstitutionApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SubstitutionApiClient(HttpClient http, ILogger<SubstitutionApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<SubstitutionDto?> GetSubstitutionAsync(string ingredient, string recipeTitle, CancellationToken ct = default)
    {
        var request = new CreateSubstitutionDto(ingredient, recipeTitle);
        var response = await _http.PostAsJsonAsync("/api/substitutions", request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw await LogAndCreateExceptionAsync(response, $"{ingredient}/{recipeTitle}");

        return await response.Content.ReadFromJsonAsync<SubstitutionDto>(cancellationToken: ct);
    }

    private async Task<HttpRequestException> LogAndCreateExceptionAsync(HttpResponseMessage response, string context = "")
    {
        var body = await response.Content.ReadAsStringAsync();
        ErrorDto? error = null;
        try { error = JsonSerializer.Deserialize<ErrorDto>(body, JsonOptions); } catch (OperationCanceledException) { throw; } catch { }

        var errorType = error?.ErrorType ?? "Unknown";
        var message = error?.Error ?? body;

        _logger.LogError("API error [{StatusCode}] {ErrorType} for {Context}: {Message}",
            (int)response.StatusCode, errorType, context, message);

        return new HttpRequestException(
            $"{errorType}: {message}",
            null,
            response.StatusCode);
    }
}
