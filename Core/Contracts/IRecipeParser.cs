using Core.Models;

namespace Core.Contracts;

public interface IRecipeParser
{
    Task<List<Recipe>> ParseRecipesAsync(string transcript, CancellationToken ct = default);
}