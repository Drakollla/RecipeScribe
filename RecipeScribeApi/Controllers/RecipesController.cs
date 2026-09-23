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
using System.Text.Json;

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
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string ingredients, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(ingredients))
            return BadRequest("ingredients is required.");

        limit = Math.Clamp(limit, 1, 50);

        var products = ingredients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var recipes = await _repository.SearchByIngredientsAsync(products, limit);
        var result = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title, r.Ingredients.Select(i => i.Name).ToList())).ToList();

        return Ok(result);
    }

    [HttpPost("extract")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult ExtractRecipe(
        [FromBody] StartExtractionDto dto,
        [FromServices] IExtractionJobManager jobManager)
    {
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest("URL не может быть пустым.");

        var job = jobManager.Enqueue(dto.Url);

        if (job == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Очередь извлечения переполнена. Подождите и попробуйте снова." });

        var responseDto = ToJobDto(job);

        return AcceptedAtAction(nameof(GetExtractionStatus), new { id = job.Id }, responseDto);
    }

    [HttpGet("extract/jobs/{id:guid}")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetExtractionStatus(
        Guid id,
        [FromServices] IExtractionJobManager jobManager)
    {
        var job = jobManager.GetJob(id);
        if (job == null)
            return NotFound(new { message = "Задача не найдена" });

        return Ok(ToJobDto(job));
    }

    [HttpPost("extract/jobs/{id:guid}/cancel")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CancelExtraction(Guid id, [FromServices] IExtractionJobManager jobManager)
    {
        var job = jobManager.CancelJob(id);
        
        if (job == null)
            return NotFound(new { message = "Задача не найдена" });

        if (job.Status != ExtractionStatus.Cancelled)
            return Conflict(new { message = "Завершённые задачи нельзя отменить." });

        return Ok(ToJobDto(job));
    }

    [HttpPost("extract/jobs/{id:guid}/resume")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult ResumeExtraction(
        Guid id,
        [FromServices] IExtractionJobManager jobManager)
    {
        var job = jobManager.GetJob(id);
        if (job == null)
            return NotFound(new { message = "Задача не найдена" });

        if (job.Status != ExtractionStatus.Cancelled)
            return Conflict(new { message = "Возобновить можно только отменённую задачу." });

        if (jobManager.ResumeJob(id) == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Очередь извлечения переполнена. Подождите и попробуйте снова." });

        return Ok(ToJobDto(job));
    }

    private static ExtractionJobDto ToJobDto(ExtractionJob job) =>
        new(job.Id, job.Url, job.Status, job.ProgressMessage, job.RecipeIds, job.Error, job.CreatedAt);

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

    [HttpGet("{id:guid}/markdown")]
    public async Task<IActionResult> GetMarkdown(Guid id)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Content(RecipeMarkdownBuilder.Build(recipe), "text/markdown", Encoding.UTF8);
    }

    [HttpGet("{id:guid}/text")]
    public async Task<IActionResult> GetPlainText(Guid id)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Content(RecipeTextBuilder.Build(recipe), "text/plain", Encoding.UTF8);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _repository.DeleteRecipeAsync(id);
        return NoContent();
    }
}
