using Core.Models;

namespace Core.Contracts;

public interface IScalingService
{
    Task<List<Ingredient>> ScaleIngredientsAsync(Recipe recipe, int targetServings, CancellationToken ct = default);
}