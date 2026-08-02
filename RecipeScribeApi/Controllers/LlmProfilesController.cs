using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Contracts;
using Core.Exceptions;
using Core.Models;
using Microsoft.AspNetCore.Mvc;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/llm")]
[ApiController]
public class LlmProfilesController : ControllerBase
{
    private readonly ILlmProfileStore _profileStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmProfilesController> _logger;

    public LlmProfilesController(
        ILlmProfileStore profileStore,
        IConfiguration configuration,
        ILogger<LlmProfilesController> logger)
    {
        _profileStore = profileStore;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("profiles")]
    public IActionResult GetProfiles()
    {
        var profiles = _profileStore.GetAll();

        return Ok(new LlmProfilesDto(
            Profiles: profiles.Select(p => new LlmProfileDto(p.Name, p.Endpoint, p.ModelId)).ToList(),
            Active: new LlmProfileDto(
                Name: null,
                Endpoint: _configuration["LlmSettings:Endpoint"] ?? "",
                ModelId: _configuration["LlmSettings:ModelId"] ?? "")
        ));
    }

    [HttpPost("profiles")]
    public IActionResult SaveProfile([FromBody] LlmProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new BadRequestException("Profile name is required.");

        if (string.IsNullOrWhiteSpace(dto.Endpoint) || string.IsNullOrWhiteSpace(dto.ModelId))
            throw new BadRequestException("Endpoint and model ID are required.");

        _profileStore.Save(new LlmProfile
        {
            Name = dto.Name,
            Endpoint = dto.Endpoint.Trim(),
            ModelId = dto.ModelId.Trim()
        });

        return Ok();
    }

    [HttpDelete("profiles/{name}")]
    public IActionResult DeleteProfile(string name)
    {
        _profileStore.Delete(name);
        return Ok();
    }

    [HttpPatch("profiles/active")]
    public IActionResult SetActive([FromBody] SetActiveLlmDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new BadRequestException("Profile name is required.");

        var profile = _profileStore.Get(dto.Name)
            ?? throw new BadRequestException($"LLM profile '{dto.Name}' not found.");

        UpdateAppSettings(profile);

        return Ok();
    }

    private void UpdateAppSettings(LlmProfile profile)
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        var json = JsonNode.Parse(System.IO.File.ReadAllText(settingsPath)) as JsonObject
            ?? throw new InvalidOperationException("Could not read appsettings.json.");

        var llmSection = json["LlmSettings"] as JsonObject ?? new JsonObject();
        llmSection["Endpoint"] = profile.Endpoint;
        llmSection["ModelId"] = profile.ModelId;
        json["LlmSettings"] = llmSection;

        System.IO.File.WriteAllText(settingsPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        _logger.LogInformation("Active LLM profile switched to {Profile}: {Model}", profile.Name, profile.ModelId);
    }
}
