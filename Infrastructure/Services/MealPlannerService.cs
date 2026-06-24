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
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services
{
    public class MealPlannerService : IMealPlannerService
    {
        private readonly RecipeDbContext _dbContext;
        private readonly Kernel _kernel;
        private readonly IOptions<LlmSettings> _llmSettings;

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
            {
                _dbContext.MealPlans.Remove(existingPlan);
            }

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

            var availableRecipes = await _dbContext.Recipes
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    Ingredients = r.Ingredients.Select(i => i.Name).ToList()
                })
                .ToListAsync();

            if (!availableRecipes.Any())
                throw new InvalidOperationException("В базе данных еще нет сохраненных рецептов.");

            var recipesJson = JsonSerializer.Serialize(availableRecipes, new JsonSerializerOptions { WriteIndented = false });

            string promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "MealPlanner.md");
            string promptTemplate = await File.ReadAllTextAsync(promptPath);

            string prompt = promptTemplate
                .Replace("{recipesList}", recipesJson)
                .Replace("{userRequest}", userRequest)
                .Replace("{targetLanguage}", _llmSettings.Value.TargetLanguage); 

            var arguments = new KernelArguments
            {
                { "recipesList", recipesJson },
                { "userRequest", userRequest }
            };

            var result = await _kernel.InvokePromptAsync(prompt, arguments);
            var rawResponse = result.ToString();

            var cleanJson = rawResponse
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            LlmMealPlanResponse? llmResponse;

            try
            {
                llmResponse = JsonSerializer.Deserialize<LlmMealPlanResponse>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Не удалось разобрать ответ от нейросети. Сырой ответ: {rawResponse}", ex);
            }

            if (llmResponse == null)
            {
                throw new InvalidOperationException("Нейросеть вернула пустой ответ.");
            }

            var mealRecipes = new Dictionary<MealType, Guid>();

            if (Guid.TryParse(llmResponse.Breakfast, out var breakfastId))
                mealRecipes[MealType.Breakfast] = breakfastId;

            if (Guid.TryParse(llmResponse.Lunch, out var lunchId))
                mealRecipes[MealType.Lunch] = lunchId;

            if (Guid.TryParse(llmResponse.Dinner, out var dinnerId))
                mealRecipes[MealType.Dinner] = dinnerId;

            if (!mealRecipes.Any())
                throw new InvalidOperationException("Нейросеть не смогла подобрать рецепты из вашего списка.");

            return await CreatePlanManualAsync(telegramChatId, date, mealRecipes);
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

                var fallbackBuilder = new StringBuilder();
                fallbackBuilder.AppendLine("*СПИСОК ПОКУПОК (без сортировки по отделам):*");
                fallbackBuilder.AppendLine("=========================");
                fallbackBuilder.AppendLine(flatListString);
                fallbackBuilder.AppendLine();
                fallbackBuilder.AppendLine("*Не удалось распределить по отделам из-за временного сбоя сети.*");

                return fallbackBuilder.ToString();
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
