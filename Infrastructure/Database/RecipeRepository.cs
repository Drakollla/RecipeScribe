using Core.Contracts;
using Core.Models;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace RecipeScribe.Infrastructure.Database
{
    public class RecipeRepository : RepositoryBase<Recipe>, IRecipeRepository
    {
        public RecipeRepository(RecipeDbContext context) : base(context)
        {
            context.Database.EnsureCreated();
        }

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

            var terms = searchProducts.Select(p => p.Trim().ToLower()).ToList();

            var filteredRecipes = await FindAll(trackChanges: false)
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .Where(r => r.Ingredients.Any(i => terms.Any(term => i.Name.ToLower().Contains(term))))
                .ToListAsync();

            var sortedRecipes = filteredRecipes
                .Select(recipe => new
                {
                    Recipe = recipe,
                    MatchCount = terms.Count(term => recipe.Ingredients.Any(i => i.Name.ToLower().Contains(term)))
                })
                .OrderByDescending(x => x.MatchCount)
                .Select(x => x.Recipe)
                .Take(limit)
                .ToList();

            return sortedRecipes;
        }

        public async Task<Recipe?> GetRecipeByIdAsync(Guid id)
        {
            return await Context.Recipes
                .Include(r => r.Ingredients)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id);
        }
    }
}