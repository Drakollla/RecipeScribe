using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class SubstituteIngredientDtoValidator : AbstractValidator<SubstituteIngredientDto>
{
    public const int MaxIngredientLength = 100;

    public SubstituteIngredientDtoValidator()
    {
        RuleFor(x => x.Ingredient)
            .NotNull()
            .NotEmpty()
            .WithMessage("Ingredient is required.")
            .MaximumLength(MaxIngredientLength)
            .WithMessage($"Ingredient must not exceed {MaxIngredientLength} characters.");
    }
}