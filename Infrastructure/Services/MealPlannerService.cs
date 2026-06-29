using Core.Contracts;
using Core.Enums;
using Core.Helpers;
using Core.Models;
using Infrastructure.Database;
using Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class MealPlannerService : IMealPlannerService
    {
        private readonly RecipeDbContext _dbContext;
        private readonly Kernel _kernel;
        private readonly IOptions<LlmSettings> _llmSettings;

        private record RecipeCandidateDto(Guid Id, string Title);

        public MealPlannerService(RecipeDbContext dbContext,
            Kernel kernel,
            IOptions<LlmSettings> llmSettings)
        {
            _dbContext = dbContext;
            _kernel = kernel;
            _llmSettings = llmSettings;
        }

        public async Task<MealPlan> CreatePlanManualAsync(long telegramChatId, DateOnly date, Dictionary<MealType, Guid> mealRecipes)
        {
            var user = await GetOrCreateUserAsync(telegramChatId);

            var existingPlan = await GetPlanForDateAsync(telegramChatId, date);

            if (existingPlan != null)
                _dbContext.MealPlans.Remove(existingPlan);

            var newPlan = new MealPlan
            {
                Id = Guid.NewGuid(),
                Date = date,
                UserId = user.Id
            };

            foreach (var (mealType, recipeId) in mealRecipes)
            {
                newPlan.Items.Add(new MealPlanItem
                {
                    Id = Guid.NewGuid(),
                    RecipeId = recipeId,
                    MealType = mealType,
                    Portions = 1
                });
            }

            _dbContext.MealPlans.Add(newPlan);
            await _dbContext.SaveChangesAsync();

            return await _dbContext.MealPlans
                .Include(mp => mp.Items)
                    .ThenInclude(mpi => mpi.Recipe)
                .FirstAsync(mp => mp.Id == newPlan.Id);
        }

        public async Task<MealPlan> GenerateSmartPlanAsync(long telegramChatId, DateOnly date, string userRequest)
        {
            var user = await GetOrCreateUserAsync(telegramChatId);

            var recentRecipeIds = await GetRecentRecipeIdsAsync(user.Id, date);
            string? primaryKeyword = GetPrimaryKeyword(userRequest);

            var breakfasts = await GetCategoryCandidatesAsync(r => r.IsBreakfast, recentRecipeIds, primaryKeyword);
            var lunches = await GetCategoryCandidatesAsync(r => r.IsLunch, recentRecipeIds, primaryKeyword);
            var dinners = await GetCategoryCandidatesAsync(r => r.IsDinner, recentRecipeIds, primaryKeyword);

            var allCandidates = breakfasts.Concat(lunches).Concat(dinners).DistinctBy(r => r.Id).ToList();

            if (!allCandidates.Any())
                throw new InvalidOperationException("В базе данных нет рецептов, соответствующих категориям приемов пищи.");

            var llmResponse = await AskLlmForPlanAsync(allCandidates, userRequest);
            var mealRecipes = new Dictionary<MealType, Guid>();

            if (Guid.TryParse(llmResponse.Breakfast, out var breakfastId))
                mealRecipes[MealType.Breakfast] = breakfastId;

            if (Guid.TryParse(llmResponse.Lunch, out var lunchId))
                mealRecipes[MealType.Lunch] = lunchId;

            if (Guid.TryParse(llmResponse.Dinner, out var dinnerId))
                mealRecipes[MealType.Dinner] = dinnerId;

            return await CreatePlanManualAsync(telegramChatId, date, mealRecipes);
        }

        private async Task<LlmMealPlanResponse> AskLlmForPlanAsync(List<RecipeCandidateDto> candidates, string userRequest)
        {
            var recipesJson = JsonSerializer.Serialize(candidates, new JsonSerializerOptions { WriteIndented = false });
            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "MealPlanner.md");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string prompt = promptTemplate
                .Replace("{recipesList}", recipesJson)
                .Replace("{userRequest}", userRequest)
                .Replace("{targetLanguage}", _llmSettings.Value.TargetLanguage);

            var rawResponse = await CallLlmAsync(prompt);
            var cleanJson = rawResponse.Replace("```json", "").Replace("```", "").Trim();

            try
            {
                var response = JsonSerializer.Deserialize<LlmMealPlanResponse>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return response ?? throw new InvalidOperationException("Нейросеть вернула пустой ответ.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Не удалось разобрать ответ от ИИ: {rawResponse}", ex);
            }
        }


        private async Task<List<RecipeCandidateDto>> GetCategoryCandidatesAsync(Expression<Func<Recipe, bool>> categoryPredicate,
            List<Guid> recentRecipeIds, string? primaryKeyword)
        {
            var query = _dbContext.Recipes
                .Where(categoryPredicate)
                .Where(r => !recentRecipeIds.Contains(r.Id));

            if (!string.IsNullOrEmpty(primaryKeyword))
            {
                query = query.OrderByDescending(r =>
                    r.Title.Contains(primaryKeyword) ||
                    r.Ingredients.Any(i => i.Name.Contains(primaryKeyword)));
            }
            else query = query.OrderBy(r => EF.Functions.Random());

            var result = await query
                .Take(10)
                .Select(r => new RecipeCandidateDto(r.Id, r.Title))
                .ToListAsync();

            if (!result.Any())
            {
                result = await _dbContext.Recipes
                    .Where(categoryPredicate)
                    .OrderBy(r => EF.Functions.Random())
                    .Take(10)
                    .Select(r => new RecipeCandidateDto(r.Id, r.Title))
                    .ToListAsync();
            }

            return result;
        }

        private string? GetPrimaryKeyword(string userRequest)
        {
            var keywords = userRequest.ToLower()
                .Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToList();

            return keywords.FirstOrDefault();
        }

        private async Task<List<Guid>> GetRecentRecipeIdsAsync(Guid userId, DateOnly date)
        {
            var startDate = date.AddDays(-3);
            var endDate = date.AddDays(3);

            return await _dbContext.MealPlanItems
                .Where(mpi => mpi.MealPlan.UserId == userId && mpi.MealPlan.Date >= startDate && mpi.MealPlan.Date <= endDate)
                .Select(mpi => mpi.RecipeId)
                .Distinct()
                .ToListAsync();
        }

        private async Task<string> CallLlmAsync(string prompt)
        {
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddUserMessage(prompt);

            var response = await chatService.GetChatMessageContentAsync(history, null, _kernel);
            return response.Content ?? string.Empty;
        }

        public async Task<MealPlan?> GetPlanForDateAsync(long telegramChatId, DateOnly date)
        {
            return await _dbContext.MealPlans
               .Include(x => x.User)
               .Include(x => x.Items)
                   .ThenInclude(x => x.Recipe)
               .FirstOrDefaultAsync(x => x.User.TelegramChatId == telegramChatId && x.Date == date);
        }

        public async Task<string> GetShoppingListAsync(Guid mealPlanId)
        {
            var planItems = await _dbContext.MealPlanItems
                .Where(mpi => mpi.MealPlanId == mealPlanId)
                .Include(mpi => mpi.Recipe)
                    .ThenInclude(r => r.Ingredients)
                .ToListAsync();

            var allIngredients = planItems
                .SelectMany(mpi => mpi.Recipe.Ingredients)
                .ToList();

            if (!allIngredients.Any())
                return "*Список покупок пуст.*";

            var flatList = allIngredients
                .GroupBy(i => i.Name.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var amounts = g.Select(i => i.Amount.Trim())
                                   .Where(a => !string.IsNullOrWhiteSpace(a))
                                   .Distinct()
                                   .ToList();

                    var name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(g.Key);
                    var amountText = amounts.Any() ? $" ({string.Join(" + ", amounts)})" : "";

                    return $"{name}{amountText}";
                })
                .OrderBy(item => item)
                .ToList();

            var flatListString = string.Join("\n", flatList.Select(item => $"• {item}"));

            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "ShoppingListCategorizer.md");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string prompt = promptTemplate
                .Replace("{flatListString}", flatListString)
                .Replace("{targetLanguage}", _llmSettings.Value.TargetLanguage);

            try
            {
                var rawResponse = await CallLlmAsync(prompt);
                var categorizedList = rawResponse.Trim();

                if (string.IsNullOrWhiteSpace(categorizedList))
                {
                    throw new Exception("ИИ вернул пустой ответ (null or whitespace).");
                }

                return $"*СПИСОК ПОКУПОК ПО ОТДЕЛАМ:\n\n{categorizedList}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ошибка ИИ при категоризации списка покупок]: {ex.Message}");

                var listOfIngredients = new StringBuilder();
                listOfIngredients.AppendLine("*СПИСОК ПОКУПОК (без сортировки по отделам):*");
                listOfIngredients.AppendLine("=========================");
                listOfIngredients.AppendLine(flatListString);
                listOfIngredients.AppendLine();
                listOfIngredients.AppendLine("*Не удалось распределить по отделам из-за временного сбоя сети.*");

                return listOfIngredients.ToString();
            }
        }

        private async Task<User> GetOrCreateUserAsync(long telegramChatId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TelegramChatId == telegramChatId);

            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    TelegramChatId = telegramChatId,
                    Username = $"tg_{telegramChatId}"
                };

                _dbContext.Users.Add(user);

                await _dbContext.SaveChangesAsync();
            }

            return user;
        }
    }
}
