using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
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
    private readonly IMealPlanRepository _mealPlanRepo;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        IRecipeRepository repository,
        IRecipeExtractorService extractor,
        IScalingService scalingService,
        IIngredientSubstitutor substitutor,
        IMealPlanRepository mealPlanRepo,
        ILogger<RecipesController> logger)
    {
        _repository = repository;
        _extractor = extractor;
        _scalingService = scalingService;
        _substitutor = substitutor;
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
    public async Task<IActionResult> GetById(Guid id, [FromQuery] int? servings = null, CancellationToken ct = default)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var targetServings = servings ?? recipe.Servings;

        if (targetServings is < 1 or > 20)
            throw new BadRequestException("Servings must be between 1 and 20.");

        if (targetServings != recipe.Servings)
        {
            var scaledIngredients = await _scalingService.ScaleIngredientsAsync(recipe, targetServings, ct);
            return Ok(recipe.ToDto() with
            {
                Servings = targetServings,
                Ingredients = scaledIngredients.Select(i => new IngredientDto(i.Name, i.Amount)).ToList()
            });
        }

        return Ok(recipe.ToDto());
    }

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
            return BadRequest(new { error = "Obsidian vault path is not configured." });

        if (!Path.IsPathRooted(vaultPath))
            return BadRequest(new { error = "Obsidian vault path must be an absolute path (e.g., D:\\обсидиан\\Заметки\\Заметки\\Рецепты)." });

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

        var suggestions = await _substitutor.GetSuggestionsAsync(dto.Ingredient, recipe.Title);

        return Ok(new SubstitutionSuggestionsDto(
            suggestions.Select(s => new SuggestionDto(s.Name, s.Description)).ToList()
        ));
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
