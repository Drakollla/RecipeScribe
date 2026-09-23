using Core.Enums;

namespace Core.Models;
public class ExtractionJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Pending;
    public string? ProgressMessage { get; set; }
    public List<Guid> RecipeIds { get; set; } = new();
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
}
