using Core.Contracts;
using Core.Enums;
using Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class RecipesController : ControllerBase
{
    private readonly IRecipeRepository _repository;
    private readonly IRecipeExtractorService _extractor;
    private readonly ILogger<RecipesController> _logger;

    public RecipesController(
        IRecipeRepository repository,
        IRecipeExtractorService extractor,
        ILogger<RecipesController> logger)
    {
        _repository = repository;
        _extractor = extractor;
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
}
