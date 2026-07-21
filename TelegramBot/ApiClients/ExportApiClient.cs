using Shared.DTOs;
using System.Text.Json;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class ExportApiClient : IExportApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ExportApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ExportApiClient(HttpClient http, ILogger<ExportApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> ExportToObsidianAsync(Guid recipeId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/recipes/{recipeId}/export-to-obsidian", null, ct);

        if (response.IsSuccessStatusCode)
            return true;

        var body = await response.Content.ReadAsStringAsync(ct);
        ErrorDto? error = null;
        
        try 
        { 
            error = JsonSerializer.Deserialize<ErrorDto>(body, JsonOptions); 
        } 
        catch (OperationCanceledException) 
        { 
            throw; 
        } 
        catch { }

        var errorType = error?.ErrorType ?? "Unknown";
        var message = error?.Error ?? body;

        _logger.LogError("Export to Obsidian failed [{StatusCode}] {ErrorType}: {Message}",
            (int)response.StatusCode, errorType, message);

        throw new HttpRequestException(
            $"{errorType}: {message}",
            null,
            response.StatusCode);
    }
}
