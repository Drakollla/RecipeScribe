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
        var result = await _substitutor.GetSubstitutionsAsync(dto.Ingredient, dto.RecipeTitle);

        return Ok(new SubstitutionDto(result));
    }
}
