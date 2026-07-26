using Core.Models;

namespace Core.Contracts;

public interface IIngredientSubstitutor
{
    Task<List<SubstitutionSuggestion>> GetSuggestionsAsync(string ingredient, string recipeTitle, CancellationToken cancellationToken = default);
}
