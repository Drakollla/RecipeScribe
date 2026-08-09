using Core.Contracts;
using Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database;

public class RecipeRepository : RepositoryBase<Recipe>, IRecipeRepository
{
    public RecipeRepository(RecipeDbContext context) : base(context) { }

    public async Task SaveRecipeAsync(Recipe recipe)
    {
        var exists = await FindByCondition(r => r.Id == recipe.Id, trackChanges: false)
            .AnyAsync();

        if (exists)
            Update(recipe);
        else await CreateAsync(recipe);

        await SaveAsync();
    }

    public async Task<List<Recipe>> GetAllRecipesAsync()
    {
        return await FindAll(trackChanges: false)
            .AsSplitQuery()
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

        var recipes = await FindByCondition(r => matchedIds
            .Contains(r.Id), trackChanges: false)
            .AsSplitQuery()
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
        return await FindByCondition(r => r.Id == id, trackChanges: false)
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Recipe>> GetVariantsAsync(Guid sourceId)
    {
        return await FindByCondition(r => r.SourceId == sourceId, trackChanges: false)
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .ToListAsync();
    }

    public async Task<Recipe?> GetSourceAsync(Guid id)
    {
        var recipe = await FindByCondition(r => r.Id == id, trackChanges: false)
            .FirstOrDefaultAsync();

        if (recipe?.SourceId is null)
            return recipe;

        return await FindByCondition(r => r.Id == recipe.SourceId, trackChanges: false)
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Recipe>> GetRecipesByUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new List<Recipe>();

        string targetUrl = url.Trim();

        return await FindByCondition(r => r.VideoUrl == targetUrl, trackChanges: false)
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .ToListAsync();
    }

    public async Task DeleteRecipeAsync(Guid id)
    {
        var recipe = await FindByCondition(r => r.Id == id, trackChanges: true)
            .FirstOrDefaultAsync();

        if (recipe == null)
            return;

        var idsToDelete = new List<Guid> { recipe.Id };

        if (recipe.SourceId is null)
        {
            var variants = await FindByCondition(r => r.SourceId == recipe.Id, trackChanges: false).ToListAsync();
            idsToDelete.AddRange(variants.Select(v => v.Id));
        }

        var planItems = await Context.MealPlanItems
            .Where(mpi => idsToDelete.Contains(mpi.RecipeId))
            .ToListAsync();

        Context.MealPlanItems.RemoveRange(planItems);

        await SaveAsync();

        foreach (var idToDelete in idsToDelete)
        {
            var entity = await FindByCondition(r => r.Id == idToDelete, trackChanges: true).FirstOrDefaultAsync();
            if (entity != null)
                Delete(entity);
        }

        await SaveAsync();
    }
}