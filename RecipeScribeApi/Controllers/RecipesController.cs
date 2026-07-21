using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Core.Helpers;
using Infrastructure.Settings;
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
    private readonly ObsidianSettings _obsidianSettings;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        IRecipeRepository repository,
        IRecipeExtractorService extractor,
        ObsidianSettings obsidianSettings,
        ILogger<RecipesController> logger)
    {
        _repository = repository;
        _extractor = extractor;
        _obsidianSettings = obsidianSettings;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var recipes = await _repository.GetAllRecipesAsync();
        var dtos = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title)).ToList();
        
        return Ok(dtos);
    }

    [HttpGet("{id:guid}", Name = "GetRecipeById")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Ok(recipe.ToDto());
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string ingredients, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(ingredients))
            return BadRequest("ingredients is required.");

        var products = ingredients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var recipes = await _repository.SearchByIngredientsAsync(products, limit);
        var result = recipes.Select(r => new RecipeSummaryDto(r.Id, r.Title)).ToList();

        return Ok(result);
    }

    [HttpPost("extract")]
    public async Task<IActionResult> Extract([FromBody] CreateRecipeDto dto)
    {
        _logger.LogInformation("Extracting recipe from {Url}", dto.Url);
        var recipe = await _extractor.ExtractAndSaveRecipeAsync(dto.Url)
            ?? throw new RecipeScribeException(ErrorType.ParseError, "Failed to extract recipe.");

        return CreatedAtRoute("GetRecipeById", new { id = recipe.Id }, recipe.ToDto());
    }

    [HttpPost("{id:guid}/export-to-obsidian")]
    public async Task<IActionResult> ExportToObsidian(Guid id)
    {
        var recipe = await _repository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var vaultPath = _obsidianSettings.VaultPath;

        if (string.IsNullOrWhiteSpace(vaultPath))
            return BadRequest(new { error = "Obsidian vault path is not configured." });

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
}
