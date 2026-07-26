using Core.Contracts;
using Microsoft.AspNetCore.Mvc;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/substitutions")]
[ApiController]
public class SubstitutionsController : ControllerBase
{
    private readonly IIngredientSubstitutor _substitutor;

    public SubstitutionsController(IIngredientSubstitutor substitutor)
    {
        _substitutor = substitutor;
    }

    [HttpPost]
    public async Task<IActionResult> Substitute([FromBody] CreateSubstitutionDto dto)
    {
        var suggestions = await _substitutor.GetSuggestionsAsync(dto.Ingredient, dto.RecipeTitle);
        var result = string.Join("\n", suggestions.Select((s, i) => $"{i + 1}. {s.Name} — {s.Description}"));

        return Ok(new SubstitutionDto(result));
    }
}
