namespace Shared.DTOs;

public record ErrorDto(string Error, string? Detail = null, string? ErrorType = null);
