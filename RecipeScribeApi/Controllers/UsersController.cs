using Core.Contracts;
using Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Shared.DTOs;

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
        var user = await _repo.GetOrCreateUserAsync(chatId);

        var path = user.ObsidianVaultPath;
        if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
        {
            await _repo.UpdateUserAsync(chatId, user.DefaultServings, null);
            path = null;
        }

        return Ok(new { defaultServings = user.DefaultServings, obsidianVaultPath = path });
    }

    [HttpPatch("{chatId}/settings")]
    public async Task<IActionResult> UpdateSettings(long chatId, [FromBody] UpdateUserSettingsDto dto)
    {
        if (dto.DefaultServings < 1 || dto.DefaultServings > 20)
            throw new BadRequestException("DefaultServings must be between 1 and 20.");

        await _repo.UpdateUserAsync(chatId, dto.DefaultServings, dto.ObsidianVaultPath);
        return Ok(new { defaultServings = dto.DefaultServings, obsidianVaultPath = dto.ObsidianVaultPath });
    }
}
