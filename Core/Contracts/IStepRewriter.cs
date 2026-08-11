using Core.ValueObjects;

namespace Core.Contracts;

public interface IStepRewriter
{
    Task<StepRewriteResult> RewriteStepsAsync(
        IReadOnlyList<IngredientSubstitution> substitutions,
        string recipeTitle,
        IReadOnlyList<string>? otherIngredients,
        IReadOnlyList<string>? stepDescriptions,
        IReadOnlyList<PreparationTip>? preparationTips,
        CancellationToken cancellationToken = default);
}