using Core.Contracts;
using Core.Enums;
using Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database;

internal class MealPlanRepository : IMealPlanRepository
{
    private readonly RecipeDbContext _db;

    public MealPlanRepository(RecipeDbContext db)
    {
        _db = db;
    }

    public async Task<User> GetOrCreateUserAsync(long telegramChatId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == telegramChatId);

        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                TelegramChatId = telegramChatId,
                Username = $"tg_{telegramChatId}"
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        return user;
    }

    public async Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date)
    {
        return await _db.MealPlans
            .AsSplitQuery()
            .Include(x => x.User)
            .Include(x => x.Items)
                .ThenInclude(x => x.Recipe)
                    .ThenInclude(x => x.Ingredients)
            .FirstOrDefaultAsync(x => x.User.TelegramChatId == telegramChatId && x.Date == date);
    }

    public async Task<MealPlan> CreatePlanAsync(MealPlan plan)
    {
        var existing = await _db.MealPlans
            .FirstOrDefaultAsync(mp => mp.UserId == plan.UserId && mp.Date == plan.Date);

        if (existing != null)
            _db.MealPlans.Remove(existing);

        _db.MealPlans.Add(plan);
        await _db.SaveChangesAsync();

        return await _db.MealPlans
            .Include(mp => mp.Items)
                .ThenInclude(mpi => mpi.Recipe)
            .FirstAsync(mp => mp.Id == plan.Id);
    }

    public async Task<Recipe?> GetRecipeByMealTypeAsync(MealType mealType, List<Guid> excludeIds)
    {
        var recipes = _db.Recipes.Where(r => !excludeIds.Contains(r.Id));

        recipes = mealType switch
        {
            MealType.Breakfast => recipes.Where(r => r.IsBreakfast),
            MealType.Lunch => recipes.Where(r => r.IsLunch),
            MealType.Dinner => recipes.Where(r => r.IsDinner),
            _ => throw new ArgumentOutOfRangeException(nameof(mealType))
        };

        return await recipes
            .OrderBy(r => r.LastPlannedAt ?? DateTime.MinValue)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateRecipeLastPlannedAtAsync(Guid recipeId)
    {
        var recipe = await _db.Recipes.FindAsync(recipeId);

        if (recipe != null)
        {
            recipe.LastPlannedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateUserAsync(long telegramChatId, int defaultServings, string? obsidianVaultPath = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == telegramChatId);

        if (user is not null)
        {
            user.DefaultServings = defaultServings;

            if (obsidianVaultPath != null)
                user.ObsidianVaultPath = obsidianVaultPath;

            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<MealPlanItem>> GetPlanItemsWithRecipesAsync(Guid mealPlanId)
    {
        return await _db.MealPlanItems
            .Where(mpi => mpi.MealPlanId == mealPlanId)
            .Include(mpi => mpi.Recipe)
                .ThenInclude(r => r.Ingredients)
            .ToListAsync();
    }

    public async Task<MealPlanItem?> UpdatePlanItemPortionsAsync(Guid planItemId, int portions, string? ingredientsJson)
    {
        var item = await _db.MealPlanItems
            .Include(mpi => mpi.Recipe)
                .ThenInclude(r => r.Ingredients)
            .FirstOrDefaultAsync(mpi => mpi.Id == planItemId);

        if (item is null)
            return null;

        item.Portions = portions;

        if (ingredientsJson != null)
            item.IngredientsJson = ingredientsJson;

        await _db.SaveChangesAsync();

        return item;
    }
}
