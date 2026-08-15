using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class CreateSubstitutionDtoValidator : AbstractValidator<CreateSubstitutionDto>
{
    private const int MaxLength = 100;

    public CreateSubstitutionDtoValidator()
    {
        RuleFor(x => x.Ingredient).NotEmpty().WithMessage("Ingredient is required.")
            .MaximumLength(MaxLength).WithMessage($"Ingredient must not exceed {MaxLength} characters.");
        RuleFor(x => x.RecipeTitle).NotEmpty().WithMessage("Recipe title is required.")
            .MaximumLength(MaxLength).WithMessage($"Recipe title must not exceed {MaxLength} characters.");
    }
}
