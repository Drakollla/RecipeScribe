using Core.Contracts;
using Core.Exceptions;
using Core.Models;
using Core.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class RecipesController : ControllerBase
{
    private readonly IRecipeRepository _repository;
    private readonly IScalingService _scalingService;
    private readonly IIngredientRescaleService _rescaleService;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(IRecipeRepository repository,
        IScalingService scalingService,
        IIngredientRescaleService rescaleService,
        ILogger<RecipesController> logger)
    {
        _repository = repository;
        _scalingService = scalingService;
        _rescaleService = rescaleService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var recipes = await _repository.GetAllRecipesAsync();
        var dtos = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title, r.Ingredients.Select(i => i.Name).ToList(),
            r.IsBreakfast, r.IsLunch, r.IsDinner, r.IsSnack)).ToList();

        return Ok(dtos);
    }

    [HttpPost]
    public async Task<IActionResult> CreateManual([FromBody] SaveRecipeDto dto)
    {
        var recipe = new Recipe
        {
            Title = dto.Title.Trim(),
            VideoUrl = string.Empty,
            Servings = dto.Servings,
            IsBreakfast = dto.IsBreakfast,
            IsLunch = dto.IsLunch,
            IsDinner = dto.IsDinner,
            IsSnack = dto.IsSnack,
            SourceId = null,
            VariantTitle = null,
            Ingredients = dto.Ingredients
                .Select(i => new Ingredient { Name = i.Name.Trim(), Amount = i.Amount ?? string.Empty })
                .ToList(),
            Steps = dto.Steps
                .Select((s, index) => new RecipeStep { Number = index + 1, Description = s.Description })
                .ToList()
        };

        await _repository.SaveRecipeAsync(recipe);

        _logger.LogInformation("Manual recipe {RecipeId} created", recipe.Id);

        var variants = await _repository.GetVariantsAsync(recipe.Id);
        return Ok(recipe.ToDto(variants: recipe.ToVariantDtos(variants)));
    }

    [HttpGet("{id:guid}", Name = "GetRecipeById")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] int? servings = null, CancellationToken ct = default)
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
        var variants = await _repository.GetVariantsAsync(recipe.SourceId ?? recipe.Id);
        var variantDtos = recipe.ToVariantDtos(variants);
        var sourceRecipe = recipe.SourceId is null ? null : await _repository.GetSourceAsync(recipe.Id);

        return Ok(recipe.ToDto(
            variants: variantDtos,
            sourceRecipe: sourceRecipe is null ? null : new RecipeVariantDto(sourceRecipe.Id, sourceRecipe.Title))
            with
        {
            Servings = targetServings,
            Ingredients = ingredients
        });
    }

    [HttpPost("{id:guid}/rescale")]
    [ProducesResponseType(typeof(RecipeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RescaleByIngredient(Guid id, [FromBody] RescaleRecipeDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var constraints = dto.Constraints
            .Select(c => new IngredientConstraint(c.Ingredient, c.Available))
            .ToList();

        var ingredients = _rescaleService.ScaleByConstraints(recipe, constraints)
            .Select(i => new IngredientDto(i.Name, i.Amount))
            .ToList();

        return Ok(recipe.ToDto() with { Ingredients = ingredients });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string ingredients, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(ingredients))
            return BadRequest(new ErrorDto("ingredients is required."));

        limit = Math.Clamp(limit, 1, 50);

        var products = ingredients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var recipes = await _repository.SearchByIngredientsAsync(products, limit);
        var result = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title, r.Ingredients.Select(i => i.Name).ToList(),
            r.IsBreakfast, r.IsLunch, r.IsDinner, r.IsSnack)).ToList();

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRecipeDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id, trackChanges: true)
            ?? throw new RecipeNotFoundException(id);

        recipe.Title = dto.Title.Trim();
        recipe.Servings = dto.Servings;
        recipe.IsBreakfast = dto.IsBreakfast;
        recipe.IsLunch = dto.IsLunch;
        recipe.IsDinner = dto.IsDinner;
        recipe.IsSnack = dto.IsSnack;

        var ingredients = dto.Ingredients
            .Select(i => new Ingredient { Name = i.Name.Trim(), Amount = i.Amount ?? string.Empty })
            .ToList();
        var steps = dto.Steps
            .Select((s, index) => new RecipeStep { Number = index + 1, Description = s.Description })
            .ToList();

        await _repository.UpdateRecipeAsync(recipe, ingredients, steps);

        _logger.LogInformation("Recipe {RecipeId} updated", recipe.Id);

        var variants = await _repository.GetVariantsAsync(recipe.SourceId ?? recipe.Id);
        var sourceRecipe = recipe.SourceId is null ? null : await _repository.GetSourceAsync(recipe.Id);

        return Ok(recipe.ToDto(
            variants: recipe.ToVariantDtos(variants),
            sourceRecipe: sourceRecipe is null ? null : new RecipeVariantDto(sourceRecipe.Id, sourceRecipe.Title)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _repository.DeleteRecipeAsync(id))
            throw new RecipeNotFoundException(id);

        return NoContent();
    }
}
