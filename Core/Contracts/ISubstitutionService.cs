using Core.Models;

namespace Core.Contracts;

public interface IIngredientSubstitutor
{
    Task<List<SubstitutionSuggestion>> GetSuggestionsAsync(
        string ingredient,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients = null,
        IReadOnlyList<string>? stepDescriptions = null,
        CancellationToken cancellationToken = default);
}
