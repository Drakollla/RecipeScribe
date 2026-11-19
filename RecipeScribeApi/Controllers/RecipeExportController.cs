using Core.Contracts;
using Core.Exceptions;
using Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class RecipeExportController : ControllerBase
{
    private readonly IRecipeRepository _recipeRepository;
    private readonly IMealPlanRepository _mealPlanRepository;
    private readonly ILogger<RecipeExportController> _logger;

    public RecipeExportController(IRecipeRepository recipeRepository, 
        IMealPlanRepository mealPlanRepository, 
        ILogger<RecipeExportController> logger)
    {
        _recipeRepository = recipeRepository;
        _mealPlanRepository = mealPlanRepository;
        _logger = logger;
    }

    [HttpGet("{id:guid}/markdown")]
    public async Task<IActionResult> GetMarkdown(Guid id)
    {
        var recipe = await _recipeRepository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Content(RecipeMarkdownBuilder.Build(recipe), "text/markdown", Encoding.UTF8);
    }

    [HttpGet("{id:guid}/text")]
    public async Task<IActionResult> GetPlainText(Guid id)
    {
        var recipe = await _recipeRepository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        return Content(RecipeTextBuilder.Build(recipe), "text/plain", Encoding.UTF8);
    }

    [HttpPost("{id:guid}/export-to-obsidian")]
    public async Task<IActionResult> ExportToObsidian(Guid id, [FromQuery] long chatId = 0)
    {
        var recipe = await _recipeRepository.GetRecipeByIdAsync(id)
            ?? throw new RecipeNotFoundException(id);

        var user = await _mealPlanRepository.GetOrCreateUserAsync(chatId);
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
}