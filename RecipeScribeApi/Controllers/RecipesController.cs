using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Core.Models;
using Core.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;
using System.Text;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class RecipesController : ControllerBase
{
    private readonly IRecipeRepository _repository;
    private readonly IRecipeExtractorService _extractor;
    private readonly IScalingService _scalingService;
    private readonly IIngredientSubstitutor _substitutor;
    private readonly IStepRewriter _stepRewriter;
    private readonly IMealPlanRepository _mealPlanRepo;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        IRecipeRepository repository,
        IRecipeExtractorService extractor,
        IScalingService scalingService,
        IIngredientSubstitutor substitutor,
        IStepRewriter stepRewriter,
        IMealPlanRepository mealPlanRepo,
        ILogger<RecipesController> logger)
    {
        _repository = repository;
        _extractor = extractor;
        _scalingService = scalingService;
        _substitutor = substitutor;
        _stepRewriter = stepRewriter;
        _mealPlanRepo = mealPlanRepo;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var recipes = await _repository.GetAllRecipesAsync();
        var dtos = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title, r.Ingredients.Select(i => i.Name).ToList())).ToList();
        
        return Ok(dtos);
    }

    [HttpGet("{id:guid}", Name = "GetRecipeById")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] int? servings = null, [FromQuery] Guid? itemId = null, CancellationToken ct = default)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var targetServings = servings ?? recipe.Servings;

        if (targetServings is < 1 or > 20)
            throw new BadRequestException("Servings must be between 1 and 20.");

        List<Ingredient> baseIngredients;
        if (targetServings != recipe.Servings)
            baseIngredients = await _scalingService.ScaleIngredientsAsync(recipe, targetServings, ct);
        else baseIngredients = recipe.Ingredients;

        var ingredients = baseIngredients.Select(i => new IngredientDto(i.Name, i.Amount)).ToList();

        if (itemId.HasValue)
            ingredients = await ApplySavedSubstitutionsAsync(itemId.Value, ingredients);

        var variants = await GetVariantsAsync(recipe);
        var sourceRecipe = recipe.SourceId is null ? null : await _repository.GetSourceAsync(recipe.Id);

        return Ok(recipe.ToDto(
            variants: variants,
            sourceRecipe: sourceRecipe is null ? null : new RecipeVariantDto(sourceRecipe.Id, sourceRecipe.Title))
            with
        {
            Servings = targetServings,
            Ingredients = ingredients
        });
    }

    private async Task<List<RecipeVariantDto>> GetVariantsAsync(Recipe recipe)
    {
        var sourceId = recipe.SourceId ?? recipe.Id;
        var variants = await _repository.GetVariantsAsync(sourceId);

        var list = new List<RecipeVariantDto>
        {
            new(sourceId, recipe.SourceId is null ? recipe.VariantTitle : null)
        };

        list.AddRange(variants.Select(v => new RecipeVariantDto(v.Id, v.VariantTitle)));
        return list.DistinctBy(v => v.Id).ToList();
    }

    private async Task<List<IngredientDto>> ApplySavedSubstitutionsAsync(Guid itemId, List<IngredientDto> ingredients)
    {
        var item = await _mealPlanRepo.GetPlanItemByIdAsync(itemId);
        var saved = item?.IngredientsJson is null ? null : PlanItemIngredients.Deserialize(item.IngredientsJson);
        
        if (saved is null || saved.Count == 0)
            return ingredients;

        var subs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var ing in saved)
        {
            if (!string.IsNullOrWhiteSpace(ing.OriginalName) &&
                !string.Equals(ing.OriginalName, ing.Name, StringComparison.OrdinalIgnoreCase))
                subs[ing.OriginalName] = ing.Name;
        }

        if (subs.Count == 0 && saved.Count == ingredients.Count)
        {
            for (int k = 0; k < saved.Count; k++)
            {
                if (!string.Equals(NormalizeName(saved[k].Name), NormalizeName(ingredients[k].Name), StringComparison.Ordinal))
                    subs[ingredients[k].Name] = saved[k].Name;
            }
        }

        if (subs.Count == 0)
            return ingredients;

        return ingredients.Select(ing =>
        {
            if (subs.TryGetValue(ing.Name, out var replacement))
                return ing with { Name = replacement, OriginalName = ing.Name };
            return ing;
        }).ToList();
    }

    private static string NormalizeName(string name) =>
        name.Trim().ToLowerInvariant().Replace('ё', 'е');

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string ingredients, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(ingredients))
            return BadRequest("ingredients is required.");

        var products = ingredients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var recipes = await _repository.SearchByIngredientsAsync(products, limit);
        var result = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title, r.Ingredients.Select(i => i.Name).ToList())).ToList();

        return Ok(result);
    }

    [HttpPost("extract")]
    public async Task<IActionResult> Extract([FromBody] CreateRecipeDto dto)
    {
        _logger.LogInformation("Extracting recipe from {Url}", dto.Url);
        var recipes = await _extractor.ExtractAndSaveRecipeAsync(dto.Url);

        if (recipes.Count == 0)
            throw new RecipeScribeException(ErrorType.ParseError, "Failed to extract recipe.");

        return Ok(recipes.Select(r => r.ToDto()).ToList());
    }

    [HttpPost("{id:guid}/export-to-obsidian")]
    public async Task<IActionResult> ExportToObsidian(Guid id, [FromQuery] long chatId = 0)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var user = await _mealPlanRepo.GetOrCreateUserAsync(chatId);
        var vaultPath = user.ObsidianVaultPath;

        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new BadRequestException("Obsidian vault path is not configured.");

        if (!Path.IsPathRooted(vaultPath))
            throw new BadRequestException("Obsidian vault path must be an absolute path.");

        Directory.CreateDirectory(vaultPath);

        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(recipe.Title.Select(c => invalid.Contains(c) ? '_' : c));
        
        if (safeName.Length > 100) 
            safeName = safeName[..100];
        
        safeName = safeName.TrimEnd('.');
        
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "recipe";

        var fullPath = Path.Combine(vaultPath, $"{safeName}.md");
        var markdown = RecipeMarkdownBuilder.Build(recipe);
        
        await System.IO.File.WriteAllTextAsync(fullPath, markdown, Encoding.UTF8);

        _logger.LogInformation("Recipe {Id} exported to Obsidian: {Path}", id, fullPath);
        
        return Ok(new { path = fullPath });
    }

    [HttpPost("{id:guid}/substitute")]
    public async Task<IActionResult> SubstituteIngredient(Guid id, [FromBody] SubstituteIngredientDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var suggestions = await _substitutor.GetSuggestionsAsync(
            dto.Ingredient,
            recipe.Title,
            otherIngredients: recipe.Ingredients
                .Where(i => !string.Equals(i.Name, dto.Ingredient, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Name)
                .ToList(),
            stepDescriptions: recipe.Steps
                .OrderBy(s => s.Number)
                .Select(s => s.Description)
                .ToList());

        return Ok(new SubstitutionSuggestionsDto(
            suggestions.Select(s => new SuggestionDto(s.Name, s.Description)).ToList()
        ));
    }

    [HttpPost("{id:guid}/rewrite-steps")]
    public async Task<IActionResult> RewriteSteps(Guid id, [FromBody] RewriteStepsDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var steps = await _stepRewriter.RewriteStepsAsync(
            dto.Ingredient,
            dto.Replacement,
            recipe.Title,
            otherIngredients: recipe.Ingredients
                .Where(i => !string.Equals(i.Name, dto.Ingredient, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Name)
                .ToList(),
            stepDescriptions: recipe.Steps
                .OrderBy(s => s.Number)
                .Select(s => s.Description)
                .ToList());

        var result = recipe.Steps
            .OrderBy(s => s.Number)
            .Select(s => new
            {
                s.Number,
                Description = steps.FirstOrDefault(x => x.Number == s.Number)?.Description ?? s.Description
            })
            .ToList();

        return Ok(new RewrittenStepsDto(result.Select(x => new RecipeStepDto(x.Number, x.Description)).ToList()));
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<IActionResult> CreateVariant(Guid id, [FromBody] CreateVariantDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var sourceId = recipe.SourceId ?? recipe.Id;

        var variant = new Recipe
        {
            Title = recipe.Title,
            VideoUrl = recipe.VideoUrl,
            Servings = recipe.Servings,
            IsBreakfast = recipe.IsBreakfast,
            IsLunch = recipe.IsLunch,
            IsDinner = recipe.IsDinner,
            IsSnack = recipe.IsSnack,
            PreparationTips = recipe.PreparationTips,
            NutritionJson = recipe.NutritionJson,
            SourceId = sourceId,
            VariantTitle = string.IsNullOrWhiteSpace(dto.VariantTitle) ? "Вариант" : dto.VariantTitle.Trim(),
            Ingredients = dto.Ingredients.Select(i => new Ingredient { Name = i.Name, Amount = i.Amount ?? "" }).ToList(),
            Steps = dto.Steps.Select(s => new RecipeStep { Number = s.Number, Description = s.Description }).ToList()
        };

        await _repository.SaveRecipeAsync(variant);

        _logger.LogInformation("Recipe variant {VariantId} created from source {SourceId}", variant.Id, sourceId);

        var variants = await GetVariantsAsync(variant);
        return Ok(variant.ToDto(variants: variants));
    }

    [HttpGet("{id:guid}/markdown")]
    public async Task<IActionResult> GetMarkdown(Guid id)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Content(RecipeMarkdownBuilder.Build(recipe), "text/markdown", Encoding.UTF8);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _repository.DeleteRecipeAsync(id);
        return NoContent();
    }
}
