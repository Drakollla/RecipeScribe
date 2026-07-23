using System.Net.Http.Json;
using System.Text.Json;
using TelegramBot.Contracts;

namespace TelegramBot.ApiClients;

public class UserApiClient : IUserApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<UserApiClient> _logger;

    public UserApiClient(HttpClient http, ILogger<UserApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<int> UpdateSettingsAsync(long chatId, int defaultServings, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/users/{chatId}/settings", new { defaultServings }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("API error [{StatusCode}] updating settings for {ChatId}: {Body}", (int)response.StatusCode, chatId, body);
            throw new HttpRequestException($"Failed to update settings: {body}", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return json.GetProperty("defaultServings").GetInt32();
    }
}
