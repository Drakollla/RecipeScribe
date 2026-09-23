using Core.Models;

namespace Core.Contracts;
public interface IExtractionJobManager
{
    ExtractionJob? Enqueue(string url);
    ExtractionJob? GetJob(Guid jobId);
    IReadOnlyCollection<ExtractionJob> GetAllJobs();
    ExtractionJob? CancelJob(Guid jobId);
    ExtractionJob? ResumeJob(Guid jobId);
}
