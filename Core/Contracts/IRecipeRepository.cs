using Core.Models;

namespace Core.Contracts;

public interface IRecipeRepository
{
    Task SaveRecipeAsync(Recipe recipe);
    Task<List<Recipe>> GetAllRecipesAsync();
    Task<List<Recipe>> SearchByIngredientsAsync(List<string> searchProducts, int limit = 10);
    Task<Recipe?> GetRecipeByIdAsync(Guid id, bool trackChanges = false);
    Task UpdateRecipeAsync(Recipe recipe, List<Ingredient> ingredients, List<RecipeStep> steps);
    Task<List<Recipe>> GetVariantsAsync(Guid sourceId);
    Task<Recipe?> GetSourceAsync(Guid id);
    Task<List<Recipe>> GetRecipesByUrlAsync(string url);
    Task DeleteRecipeAsync(Guid id);
}