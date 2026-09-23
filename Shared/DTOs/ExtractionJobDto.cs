using Core.Enums;

namespace Shared.DTOs
{
    public record ExtractionJobDto(
        Guid Id,
        string Url,
        ExtractionStatus Status,
        string? ProgressMessage,
        List<Guid> RecipeIds,
        string? Error,
        DateTime CreatedAt);

    public record StartExtractionDto(string Url);
}
