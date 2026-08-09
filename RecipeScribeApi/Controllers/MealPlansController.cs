using Core.Contracts;
using Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/mealplans")]
[ApiController]
public class MealPlansController : ControllerBase
{
    private readonly IMealPlannerService _mealPlanner;
    private readonly IRecipeRepository _recipeRepository;

    public MealPlansController(IMealPlannerService mealPlanner, IRecipeRepository recipeRepository)
    {
        _mealPlanner = mealPlanner;
        _recipeRepository = recipeRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetPlan([FromQuery] long chatId, [FromQuery] string? date)
    {
        var targetDate = ParseDate(date);

        if (targetDate is null)
            throw new BadRequestException("Invalid date format. Use YYYY-MM-DD.");

        var plan = await _mealPlanner.GetPlanForDateAsync(chatId, targetDate.Value);

        if (plan is null)
            return NoContent();

        return Ok(await plan.ToDtoAsync(_recipeRepository));
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] CreateMealPlanDto dto, [FromQuery] long chatId)
    {
        var targetDate = ParseDate(dto.Date);

        if (targetDate is null)
            throw new BadRequestException("Invalid date format. Use YYYY-MM-DD.");

        var plan = await _mealPlanner.GenerateAutoPlanAsync(chatId, targetDate.Value, dto.Preferences ?? "");

        return CreatedAtAction(nameof(GetPlan), new { chatId, date = plan.Date.ToString("yyyy-MM-dd") }, await plan.ToDtoAsync(_recipeRepository));
    }

    [HttpPatch("items/{itemId:guid}")]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateMealPlanItemDto dto)
    {
        var item = await _mealPlanner.UpdatePlanItemAsync(itemId, dto.Portions, dto.RecipeId);

        return Ok(await item.ToDtoAsync(_recipeRepository));
    }

    [HttpGet("{id:guid}/shopping-list")]
    public async Task<IActionResult> GetShoppingList(Guid id)
    {
        var list = await _mealPlanner.GetShoppingListAsync(id);

        return Ok(list);
    }

    private static DateOnly? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return DateOnly.FromDateTime(DateTime.Today);

        if (DateOnly.TryParse(date, out var parsed))
            return parsed;

        return null;
    }
}
