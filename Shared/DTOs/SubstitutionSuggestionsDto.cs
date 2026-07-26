namespace Shared.DTOs;

public record SubstitutionSuggestionsDto(List<SuggestionDto> Suggestions);

public record SuggestionDto(string Name, string Description);
