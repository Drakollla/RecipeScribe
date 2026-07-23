namespace TelegramBot.Contracts;

public interface IUserApiClient
{
    Task<int> UpdateSettingsAsync(long chatId, int defaultServings, CancellationToken ct = default);
}
