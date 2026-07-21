namespace TelegramBot.Contracts;

public interface IExportApiClient
{
    Task<bool> ExportToObsidianAsync(Guid recipeId, CancellationToken ct = default);
}
