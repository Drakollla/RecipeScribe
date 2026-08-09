using Core.Models;

namespace Core.Contracts;

public interface IStepRewriter
{
    Task<List<RecipeStep>> RewriteStepsAsync(
        string ingredient,
        string replacement,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients,
        IReadOnlyList<string>? stepDescriptions,
        CancellationToken cancellationToken = default);
}