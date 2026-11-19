using Core.Models;
using Core.ValueObjects;

namespace Core.Contracts
{
    public interface IIngredientRescaleService
    {
        List<Ingredient> ScaleByConstraints(Recipe recipe,
            IReadOnlyCollection<IngredientConstraint> constraints);
    }
}