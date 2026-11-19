using Core.Models;
using Shared.DTOs;

namespace RecipeScribeApi.Mapping;

public static class ExtractionJobMapping
{
    public static ExtractionJobDto ToDto(this ExtractionJob job) =>
        new(job.Id, job.Url, job.Status, job.ProgressMessage, job.RecipeIds, job.Error, job.CreatedAt);
}
