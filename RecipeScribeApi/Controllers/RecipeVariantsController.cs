using Core.Contracts;
using Core.Exceptions;
using Core.Models;
using Core.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;
using System.Text.Json;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class RecipeVariantsController : ControllerBase
{
    private readonly IRecipeRepository _repository;
    private readonly IIngredientSubstitutor _substitutor;
    private readonly IStepRewriter _stepRewriter;
    private readonly ILogger<RecipeVariantsController> _logger;

    public RecipeVariantsController(IRecipeRepository repository,
        IIngredientSubstitutor substitutor,
        IStepRewriter stepRewriter,
        ILogger<RecipeVariantsController> logger)
    {
        _repository = repository;
        _substitutor = substitutor;
        _stepRewriter = stepRewriter;
        _logger = logger;
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

        var substitutions = dto.Substitutions
            .Where(s => !string.IsNullOrWhiteSpace(s.Ingredient) && !string.IsNullOrWhiteSpace(s.Replacement))
            .Select(s => new IngredientSubstitution(s.Ingredient.Trim(), s.Replacement.Trim()))
            .ToList();

        if (substitutions.Count == 0)
            throw new BadRequestException("At least one substitution is required.");

        var substitutedNames = substitutions.Select(s => s.Ingredient).ToList();
        var tips = DeserializeTips(recipe.PreparationTips);
        var result = await _stepRewriter.RewriteStepsAsync(
            substitutions,
            recipe.Title,
            otherIngredients: recipe.Ingredients
                .Select(i => i.Name)
                .Where(n => !substitutedNames.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            stepDescriptions: recipe.Steps
                .OrderBy(s => s.Number)
                .Select(s => s.Description)
                .ToList(),
            preparationTips: tips);

        var rewrittenSteps = result.Steps;

        var mergedSteps = recipe.Steps
            .OrderBy(s => s.Number)
            .Select(s => new
            {
                s.Number,
                Description = rewrittenSteps.FirstOrDefault(x => x.Number == s.Number)?.Description ?? s.Description
            })
            .ToList();

        var mergedTips = tips
            .Select((t, i) => result.Tips is not null && i < result.Tips.Count && result.Tips[i] != null ? result.Tips[i] : t)
            .Select(t => new PreparationTipDto(t.Ingredient, t.Tip))
            .ToList();

        return Ok(new RewrittenStepsDto(
            mergedSteps.Select(x => new RecipeStepDto(x.Number, x.Description)).ToList(),
            mergedTips.Count > 0 ? mergedTips : null));
    }

    private static List<PreparationTip> DeserializeTips(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<PreparationTip>();

        try
        {
            return JsonSerializer.Deserialize<List<PreparationTip>>(raw) ?? new List<PreparationTip>();
        }
        catch (JsonException)
        {
            return new List<PreparationTip>();
        }
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<IActionResult> CreateVariant(Guid id, [FromBody] CreateVariantDto dto)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var sourceId = recipe.SourceId ?? recipe.Id;

        var variantTitle = dto.VariantTitle?.Trim();

        var variant = new Recipe
        {
            Title = string.IsNullOrWhiteSpace(variantTitle) ? recipe.Title : variantTitle,
            VideoUrl = recipe.VideoUrl,
            Servings = recipe.Servings,
            IsBreakfast = recipe.IsBreakfast,
            IsLunch = recipe.IsLunch,
            IsDinner = recipe.IsDinner,
            IsSnack = recipe.IsSnack,
            PreparationTips = dto.PreparationTips is { Count: > 0 }
                ? JsonSerializer.Serialize(dto.PreparationTips.Select(t => new PreparationTip { Ingredient = t.Ingredient, Tip = t.Tip }).ToList())
                : recipe.PreparationTips,
            NutritionJson = recipe.NutritionJson,
            SourceId = sourceId,
            VariantTitle = string.IsNullOrWhiteSpace(variantTitle) ? "Вариант" : variantTitle,
            Ingredients = dto.Ingredients.Select(i => new Ingredient { Name = i.Name, Amount = i.Amount ?? "" }).ToList(),
            Steps = dto.Steps.Select(s => new RecipeStep { Number = s.Number, Description = s.Description }).ToList()
        };

        await _repository.SaveRecipeAsync(variant);

        _logger.LogInformation("Recipe variant {VariantId} created from source {SourceId}", variant.Id, sourceId);

        var variants = await _repository.GetVariantsAsync(variant.SourceId ?? variant.Id);
        return Ok(variant.ToDto(variants: variant.ToVariantDtos(variants)));
    }
}
