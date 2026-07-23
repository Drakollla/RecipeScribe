using Core.Contracts;
using Core.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace RecipeScribeApi.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IMealPlanRepository _repo;

    public UsersController(IMealPlanRepository repo)
    {
        _repo = repo;
    }

    [HttpGet("{chatId}/settings")]
    public async Task<IActionResult> GetSettings(long chatId)
    {
        var plan = await _repo.GetPlanForDateAsync(chatId, DateOnly.FromDateTime(DateTime.Today));
        var user = plan?.User;

        if (user is null)
            user = await _repo.GetOrCreateUserAsync(chatId);

        return Ok(new { defaultServings = user.DefaultServings });
    }

    [HttpPatch("{chatId}/settings")]
    public async Task<IActionResult> UpdateSettings(long chatId, [FromBody] UpdateUserSettingsDto dto)
    {
        if (dto.DefaultServings < 1 || dto.DefaultServings > 20)
            throw new BadRequestException("DefaultServings must be between 1 and 20.");

        await _repo.UpdateUserAsync(chatId, dto.DefaultServings);
        return Ok(new { defaultServings = dto.DefaultServings });
    }
}

public record UpdateUserSettingsDto(int DefaultServings);
