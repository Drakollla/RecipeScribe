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
            // TODO: попробовать векторный поиск (embeddings) вместо in-memory фильтрации
            if (searchProducts == null || !searchProducts.Any())
                return new List<Recipe>();

            var ingredientNames = await Context.Ingredients
                .Select(i => new { i.RecipeId, i.Name })
                .ToListAsync();

            var matchedIds = ingredientNames
                .Where(i => searchProducts.Any(p => i.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.RecipeId)
                .Distinct()
                .ToList();

            if (matchedIds.Count == 0)
                return new List<Recipe>();

            var recipes = await Context.Recipes
                .Where(r => matchedIds.Contains(r.Id))
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .ToListAsync();

            return recipes
                .OrderByDescending(r =>
                    searchProducts.Count(p =>
                        r.Ingredients.Any(i => i.Name.Contains(p, StringComparison.OrdinalIgnoreCase))))
                .Take(limit)
                .ToList();
        }

        public async Task<Recipe?> GetRecipeByIdAsync(Guid id)
        {
            return await Context.Recipes
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<List<Recipe>> GetRecipesByUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new List<Recipe>();

            string targetUrl = url.Trim();

            return await Context.Recipes
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .Where(r => r.VideoUrl == targetUrl)
                .ToListAsync();
        }

        public async Task DeleteRecipeAsync(Guid id)
        {
            var recipe = await Context.Recipes.FindAsync(id);
            
            if (recipe != null)
            {
                Delete(recipe);
                await SaveAsync();
            }
        }
    }
}