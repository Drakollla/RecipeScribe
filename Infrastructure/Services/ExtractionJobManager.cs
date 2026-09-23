using Core.Contracts;
using Core.Enums;
using Core.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Infrastructure.Services;
public class ExtractionJobManager : IExtractionJobManager
{
    private readonly Channel<Guid> _channel;
    private readonly ConcurrentDictionary<Guid, ExtractionJob> _jobs = new();
    private const int MaxQueueSize = 10;
    private readonly object _enqueueLock = new();

    public ExtractionJobManager()
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ExtractionJob? Enqueue(string url)
    {
        lock (_enqueueLock)
        {
            var existing = _jobs.Values.FirstOrDefault(x =>
                x.Url.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                (x.Status == ExtractionStatus.Pending || x.Status == ExtractionStatus.Processing));

            if (existing != null)
                return existing;

            var job = new ExtractionJob
            {
                Url = url,
                ProgressMessage = "В очереди..."
            };

            _jobs.TryAdd(job.Id, job);

            if (!_channel.Writer.TryWrite(job.Id))
            {
                _jobs.TryRemove(job.Id, out _);
                return null;
            }

            CleanupOldJobs();
            return job;
        }
    }

    public ExtractionJob? GetJob(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public ExtractionJob? CancelJob(Guid jobId)
    {
        var job = GetJob(jobId);

        if (job == null)
            return null;

        CancellationTokenSource? cancellation = null;

        lock (job)
        {
            if (job.Status == ExtractionStatus.Pending || job.Status == ExtractionStatus.Processing)
            {
                job.Status = ExtractionStatus.Cancelled;
                job.ProgressMessage = "Отменено";
                job.FinishedAt = DateTime.UtcNow;
                cancellation = job.Cancellation;
            }
        }

        if (cancellation != null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return job;
    }

    public ExtractionJob? ResumeJob(Guid jobId)
    {
        lock (_enqueueLock)
        {
            var job = GetJob(jobId);
            if (job == null)
                return null;

            lock (job)
            {
                if (job.Status != ExtractionStatus.Cancelled)
                    return job;

                job.Status = ExtractionStatus.Pending;
                job.ProgressMessage = "В очереди...";
                job.Error = null;
                job.FinishedAt = null;
            }

            if (_channel.Writer.TryWrite(job.Id))
            {
                CleanupOldJobs();
                return job;
            }

            lock (job)
            {
                job.Status = ExtractionStatus.Cancelled;
                job.ProgressMessage = "Отменено";
                job.FinishedAt = DateTime.UtcNow;
            }

            return null;
        }
    }

    public IReadOnlyCollection<ExtractionJob> GetAllJobs() =>
        _jobs.Values.ToList().AsReadOnly();

    public void UpdateJob(Guid jobId, Action<ExtractionJob> updateAction)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            lock (job)
            {
                updateAction(job);
            }
        }
    }

    private void CleanupOldJobs()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);

        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedAt.HasValue && job.FinishedAt < cutoff)
                _jobs.TryRemove(id, out _);
        }
    }
}
