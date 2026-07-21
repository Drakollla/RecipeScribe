using FluentValidation;
using Shared.DTOs;

namespace RecipeScribeApi.Validators;

public class CreateRecipeDtoValidator : AbstractValidator<CreateRecipeDto>
{
    public CreateRecipeDtoValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("A valid HTTP or HTTPS URL is required.");
    }
}
