using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class SaveRecipeDtoValidator : AbstractValidator<SaveRecipeDto>
{
    public const int MaxTitleLength = 300;
    public const int MaxIngredientNameLength = 200;
    public const int MaxStepLength = 3000;
    public const int MaxIngredients = 100;
    public const int MaxSteps = 100;

    public SaveRecipeDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotNull()
            .NotEmpty()
            .WithMessage("Title is required.")
            .MaximumLength(MaxTitleLength)
            .WithMessage($"Title must be at most {MaxTitleLength} characters.");

        RuleFor(x => x.Servings)
            .InclusiveBetween(1, 20)
            .WithMessage("Servings must be between 1 and 20.");

        RuleFor(x => x.Ingredients)
            .NotNull()
            .WithMessage("Ingredients are required.")
            .NotEmpty()
            .WithMessage("At least one ingredient is required.")
            .Must(x => x.Count <= MaxIngredients)
            .WithMessage($"No more than {MaxIngredients} ingredients.");

        RuleForEach(x => x.Ingredients)
            .ChildRules(ing =>
            {
                ing.RuleFor(i => i.Name)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Ingredient name is required.")
                    .MaximumLength(MaxIngredientNameLength)
                    .WithMessage($"Ingredient name must be at most {MaxIngredientNameLength} characters.");
                ing.RuleFor(i => i.Amount)
                    .NotNull()
                    .WithMessage("Ingredient amount is required (may be empty).");
            });

        RuleFor(x => x.Steps)
            .NotNull()
            .WithMessage("Steps are required.")
            .NotEmpty()
            .WithMessage("At least one step is required.")
            .Must(x => x.Count <= MaxSteps)
            .WithMessage($"No more than {MaxSteps} steps.");

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Description)
                    .NotNull()
                    .NotEmpty()
                    .WithMessage("Step description is required.")
                    .MaximumLength(MaxStepLength)
                    .WithMessage($"Step description must be at most {MaxStepLength} characters.");
            });
    }
}
