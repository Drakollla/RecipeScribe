using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class CreateMealPlanDtoValidator : AbstractValidator<CreateMealPlanDto>
{
    public CreateMealPlanDtoValidator()
    {
        RuleFor(x => x.Date)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithMessage("Invalid date format. Use YYYY-MM-DD.");
    }
}
