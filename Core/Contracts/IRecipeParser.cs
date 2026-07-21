using Core.Models;

namespace Core.Contracts
{
    public interface IRecipeParser
    {
        Task<Recipe> ParseRecipeAsync(string transcript, CancellationToken ct = default);
    }
}