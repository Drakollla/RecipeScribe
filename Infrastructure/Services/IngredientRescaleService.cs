using Core.Contracts;
using Core.Exceptions;
using Core.Helpers;
using Core.Models;
using Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class IngredientRescaleService : IIngredientRescaleService
    {
        private readonly ILogger<IngredientRescaleService> _logger;

        public IngredientRescaleService(ILogger<IngredientRescaleService> logger)
        {
            _logger = logger;
        }

        public List<Ingredient> ScaleByConstraints(Recipe recipe, IReadOnlyCollection<IngredientConstraint> constraints)
        {
            decimal? factor = null;

            foreach (var constraint in constraints)
            {
                var ingredient = recipe.Ingredients.FirstOrDefault(i =>
                    string.Equals(i.Name?.Trim(), constraint.Ingredient?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (ingredient is null)
                    continue;

                var parsed = IngredientAmountParser.Parse(ingredient.Amount);

                _logger.LogDebug("Rescale parse '{Amount}' → {Kind} qty={Qty} unit={Unit}",
                    ingredient.Amount, parsed.Kind, parsed.Quantity, parsed.Unit);

                if (parsed.Quantity is not > 0)
                    continue;

                var f = IngredientRescaler.Factor(constraint.Available, parsed.Quantity.Value);
                factor = factor is null ? f : Math.Min(factor.Value, f);
            }

            if (factor is null or <= 0)
                throw new BadRequestException("Could not parse any constrained ingredient amount.");

            _logger.LogDebug("Rescale factor {Factor} for recipe {Id}", factor, recipe.Id);

            return IngredientRescaler.ScaleIngredients(recipe.Ingredients, factor.Value);
        }
    }
}
