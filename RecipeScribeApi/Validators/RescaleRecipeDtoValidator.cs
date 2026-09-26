using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class RescaleRecipeDtoValidator : AbstractValidator<RescaleRecipeDto>
{
    public const int MaxIngredientLength = 100;
    public const int MaxConstraints = 50;

    public RescaleRecipeDtoValidator()
    {
        RuleFor(x => x.Constraints)
            .NotNull()
            .WithMessage("Constraints are required.")
            .NotEmpty()
            .WithMessage("At least one constraint is required.")
            .Must(x => x.Count <= MaxConstraints)
            .WithMessage($"No more than {MaxConstraints} constraints per request.");

        RuleForEach(x => x.Constraints)
            .Must(c => c is not null
                && !string.IsNullOrWhiteSpace(c.Ingredient)
                && c.Ingredient.Length <= MaxIngredientLength
                && c.Available > 0)
            .WithMessage($"Each constraint must have a non-empty Ingredient (at most {MaxIngredientLength} characters) and a positive Available amount.");
    }
}
