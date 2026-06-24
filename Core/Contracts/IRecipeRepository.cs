using Core.Models;

namespace Core.Contracts
{
    public interface IRecipeRepository
    {
        Task SaveRecipeAsync(Recipe recipe);
        Task<List<Recipe>> GetAllRecipesAsync();
        Task<List<Recipe>> SearchByIngredientsAsync(List<string> searchProducts, int limit = 10);
        Task<Recipe?> GetRecipeByIdAsync(Guid id);
    }
}