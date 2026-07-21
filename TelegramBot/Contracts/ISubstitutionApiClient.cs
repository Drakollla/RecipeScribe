using Shared.DTOs;

namespace TelegramBot.Contracts;

public interface ISubstitutionApiClient
{
    Task<SubstitutionDto?> GetSubstitutionAsync(string ingredient, string recipeTitle, CancellationToken ct = default);
}
