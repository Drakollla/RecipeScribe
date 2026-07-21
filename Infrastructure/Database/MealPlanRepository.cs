using Core.Contracts;
using Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Database;

internal class MealPlanRepository : IMealPlanRepository
{
    private const int RecentRecipeWindowDays = 3;
    private const int MaxCandidates = 10;

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
            .Include(x => x.User)
            .Include(x => x.Items)
                .ThenInclude(x => x.Recipe)
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

    public async Task<List<Guid>> GetRecentRecipeIdsAsync(Guid userId, DateOnly date)
    {
        var startDate = date.AddDays(-RecentRecipeWindowDays);
        var endDate = date.AddDays(RecentRecipeWindowDays);

        return await _db.MealPlanItems
            .Where(mpi => mpi.MealPlan.UserId == userId && mpi.MealPlan.Date >= startDate && mpi.MealPlan.Date <= endDate)
            .Select(mpi => mpi.RecipeId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<RecipeCandidate>> GetCategoryCandidatesAsync(
        Expression<Func<Recipe, bool>> categoryPredicate,
        List<Guid> excludeIds, string? primaryKeyword)
    {
        var query = _db.Recipes
            .Where(categoryPredicate)
            .Where(r => !excludeIds.Contains(r.Id));

        if (!string.IsNullOrEmpty(primaryKeyword))
        {
            query = query.OrderByDescending(r =>
                r.Title.Contains(primaryKeyword) ||
                r.Ingredients.Any(i => i.Name.Contains(primaryKeyword)));
        }
        else
        {
            query = query.OrderBy(r => EF.Functions.Random());
        }

        var result = await query
            .Take(MaxCandidates)
            .Select(r => new RecipeCandidate(r.Id, r.Title))
            .ToListAsync();

        if (!result.Any())
        {
            result = await _db.Recipes
                .Where(categoryPredicate)
                .OrderBy(r => EF.Functions.Random())
                .Take(MaxCandidates)
                .Select(r => new RecipeCandidate(r.Id, r.Title))
                .ToListAsync();
        }

        return result;
    }

    public async Task<List<MealPlanItem>> GetPlanItemsWithRecipesAsync(Guid mealPlanId)
    {
        return await _db.MealPlanItems
            .Where(mpi => mpi.MealPlanId == mealPlanId)
            .Include(mpi => mpi.Recipe)
                .ThenInclude(r => r.Ingredients)
            .ToListAsync();
    }
}
