using FluentValidation;
using FluentValidation.AspNetCore;

namespace RecipeScribeApi.Extensions;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation()
            .AddValidatorsFromAssemblyContaining<Program>();
        services.AddSwaggerGen();

        return services;
    }
}
