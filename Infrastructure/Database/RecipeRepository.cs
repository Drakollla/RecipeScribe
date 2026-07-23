using Core.Contracts;
using Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database
{
    public class RecipeRepository : RepositoryBase<Recipe>, IRecipeRepository
    {
        public RecipeRepository(RecipeDbContext context) : base(context) { }

        public async Task SaveRecipeAsync(Recipe recipe)
        {
            var exists = await Context.Recipes.AnyAsync(r => r.Id == recipe.Id);

            if (exists)
                Update(recipe);
            else await CreateAsync(recipe);

            await SaveAsync();
        }

        public async Task<List<Recipe>> GetAllRecipesAsync()
        {
            return await FindAll(trackChanges: false)
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .ToListAsync();
        }

        public async Task<List<Recipe>> SearchByIngredientsAsync(List<string> searchProducts, int limit = 10)
        {
            if (searchProducts == null || !searchProducts.Any())
                return new List<Recipe>();

            var recipes = await FindAll(trackChanges: false)
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .ToListAsync();

            var filteredRecipes = recipes
                .Where(r => r.Ingredients.Any(i =>
                    searchProducts.Any(p => i.Name.Contains(p, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(r =>
                    searchProducts.Count(p =>
                        r.Ingredients.Any(i => i.Name.Contains(p, StringComparison.OrdinalIgnoreCase))))
                .Take(limit)
                .ToList();

            return filteredRecipes;
        }

        public async Task<Recipe?> GetRecipeByIdAsync(Guid id)
        {
            return await Context.Recipes
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<Recipe?> GetRecipeByUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string targetUrl = url.Trim();

            return await Context.Recipes
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.VideoUrl == targetUrl);
        }
    }
}