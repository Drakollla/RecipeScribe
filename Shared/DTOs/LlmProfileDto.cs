namespace Shared.DTOs;

public record LlmProfileDto(string? Name, string Endpoint, string ModelId);

public record LlmProfilesDto(List<LlmProfileDto> Profiles, LlmProfileDto Active);

public record SetActiveLlmDto(string Name);
