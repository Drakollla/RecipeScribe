using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class CreateVariantDtoValidator : AbstractValidator<CreateVariantDto>
{
    public const int MaxIngredientNameLength = 100;
    public const int MaxStepDescriptionLength = 4000;

    public CreateVariantDtoValidator()
    {
        RuleFor(x => x.Ingredients)
            .NotNull()
            .WithMessage("Ingredients are required.")
            .NotEmpty()
            .WithMessage("At least one ingredient is required.");

        RuleForEach(x => x.Ingredients)
            .Must(i => i is not null
                && !string.IsNullOrWhiteSpace(i.Name)
                && i.Name.Length <= MaxIngredientNameLength
                && (i.Amount is null || i.Amount!.Length <= MaxIngredientNameLength))
            .WithMessage($"Each ingredient must have a non-empty name (at most {MaxIngredientNameLength} characters); amount is optional.");

        RuleFor(x => x.Steps)
            .NotNull()
            .WithMessage("Steps are required.")
            .NotEmpty()
            .WithMessage("At least one step is required.");

        RuleForEach(x => x.Steps)
            .Must(s => s is not null
                && s.Number > 0
                && !string.IsNullOrWhiteSpace(s.Description)
                && s.Description.Length <= MaxStepDescriptionLength)
            .WithMessage($"Each step must have a positive number and a non-empty description (at most {MaxStepDescriptionLength} characters).");

        RuleFor(x => x.VariantTitle)
            .MaximumLength(200)
            .WithMessage("Variant title must not exceed 200 characters.");

        RuleForEach(x => x.PreparationTips)
            .Must(t => t is null
                || ((t.Ingredient is null || t.Ingredient.Length <= MaxIngredientNameLength)
                    && (t.Tip is null || t.Tip.Length <= 1000)))
            .WithMessage($"Tip must not exceed {MaxIngredientNameLength} characters for ingredient and 1000 for the tip text.");
    }
}