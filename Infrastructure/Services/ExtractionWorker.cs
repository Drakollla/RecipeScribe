using Core.Contracts;
using Core.Enums;
using Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;
public class ExtractionWorker : BackgroundService
{
    private readonly ExtractionJobManager _jobManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExtractionWorker> _logger;

    public ExtractionWorker(ExtractionJobManager jobManager,
        IServiceScopeFactory scopeFactory,
        ILogger<ExtractionWorker> logger)
    {
        _jobManager = jobManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(ExtractionJobMessages.LogStarted);

        while (await _jobManager.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_jobManager.Reader.TryRead(out var jobId))
                await ProcessJobAsync(jobId, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        var job = _jobManager.GetJob(jobId);

        if (job == null || job.Status == ExtractionStatus.Cancelled)
            return;

        _logger.LogInformation(ExtractionJobMessages.LogJobStarted, jobId, job.Url);
        MarkProcessing(jobId);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _jobManager.UpdateJob(jobId, j => j.Cancellation = cts);

        if (job.Status == ExtractionStatus.Cancelled)
            cts.Cancel();

        try
        {
            var recipes = await ExtractRecipesAsync(job, jobId, cts.Token);
            MarkCompleted(jobId, recipes);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            MarkInterrupted(jobId);
        }
        catch (Exception ex)
        {
            MarkFailed(jobId, ex);
        }
        finally
        {
            _jobManager.UpdateJob(jobId, j => j.Cancellation = null);
        }
    }

    private async Task<List<Recipe>> ExtractRecipesAsync(ExtractionJob job, Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<IRecipeExtractorService>();

        var recipes = await extractor.ExtractAndSaveRecipeAsync(
            job.Url,
            onProgress: progressText =>
            {
                _jobManager.UpdateJob(jobId, j => j.ProgressMessage = progressText);
                return Task.CompletedTask;
            },
            cancellationToken: ct
        );

        _logger.LogInformation(
            ExtractionJobMessages.LogJobCompleted,
            jobId,
            recipes.Count,
            string.Join(", ", recipes.Select(r => r.Id)));

        return recipes;
    }

    private void MarkProcessing(Guid jobId)
    {
        _jobManager.UpdateJob(jobId, j =>
        {
            j.Status = ExtractionStatus.Processing;
            j.ProgressMessage = ExtractionJobMessages.MessageProcessing;
        });
    }

    private void MarkCompleted(Guid jobId, List<Recipe> recipes)
    {
        _jobManager.UpdateJob(jobId, j =>
        {
            j.Status = ExtractionStatus.Completed;
            j.ProgressMessage = ExtractionJobMessages.MessageCompleted;
            j.RecipeIds = recipes.Select(r => r.Id).ToList();
            j.FinishedAt = DateTime.UtcNow;
        });
    }

    private void MarkInterrupted(Guid jobId)
    {
        _logger.LogWarning(ExtractionJobMessages.LogJobInterrupted, jobId);
        _jobManager.UpdateJob(jobId, j =>
        {
            if (j.Status != ExtractionStatus.Cancelled)
            {
                j.Status = ExtractionStatus.Failed;
                j.Error = ExtractionJobMessages.MessageShutdown;
            }

            j.FinishedAt ??= DateTime.UtcNow;
        });
    }

    private void MarkFailed(Guid jobId, Exception ex)
    {
        _logger.LogError(ex, ExtractionJobMessages.LogJobFailed, jobId);
        _jobManager.UpdateJob(jobId, j =>
        {
            j.Status = ExtractionStatus.Failed;
            j.Error = ex.Message;
            j.FinishedAt = DateTime.UtcNow;
        });
    }
}
