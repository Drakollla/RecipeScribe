namespace Shared.DTOs;

public record UpdateUserSettingsDto(int DefaultServings, string? ObsidianVaultPath = null);
