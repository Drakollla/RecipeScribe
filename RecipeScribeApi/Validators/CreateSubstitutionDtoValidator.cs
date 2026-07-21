using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class CreateSubstitutionDtoValidator : AbstractValidator<CreateSubstitutionDto>
{
    public CreateSubstitutionDtoValidator()
    {
        RuleFor(x => x.Ingredient).NotEmpty().WithMessage("Ingredient is required.");
        RuleFor(x => x.RecipeTitle).NotEmpty().WithMessage("Recipe title is required.");
    }
}
