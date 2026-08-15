using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class RewriteStepsDtoValidator : AbstractValidator<RewriteStepsDto>
{
    public const int MaxIngredientLength = 100;
    public const int MaxSubstitutions = 20;

    public RewriteStepsDtoValidator()
    {
        RuleFor(x => x.Substitutions)
            .NotNull()
            .WithMessage("Substitutions are required.")
            .NotEmpty()
            .WithMessage("At least one substitution is required.")
            .Must(x => x.Count <= MaxSubstitutions)
            .WithMessage($"No more than {MaxSubstitutions} substitutions per request.");

        RuleForEach(x => x.Substitutions)
            .Must(s => s is not null
                && !string.IsNullOrWhiteSpace(s.Ingredient)
                && s.Ingredient.Length <= MaxIngredientLength
                && !string.IsNullOrWhiteSpace(s.Replacement)
                && s.Replacement.Length <= MaxIngredientLength)
            .WithMessage($"Each substitution must have non-empty Ingredient and Replacement, each at most {MaxIngredientLength} characters.");
    }
}