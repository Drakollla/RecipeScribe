using Core.Contracts;
using Core.Enums;
using Microsoft.AspNetCore.Mvc;
using RecipeScribeApi.Mapping;
using Shared.DTOs;

namespace RecipeScribeApi.Controllers;

[Route("api/recipes")]
[ApiController]
public class ExtractionController : ControllerBase
{
    private readonly IExtractionJobManager _jobManager;

    public ExtractionController(IExtractionJobManager jobManager)
    {
        _jobManager = jobManager;
    }

    [HttpPost("extract")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult ExtractRecipe([FromBody] StartExtractionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Url))
            return BadRequest(new ErrorDto("URL cannot be empty."));

        var job = _jobManager.Enqueue(dto.Url);

        if (job == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorDto("Extraction queue is full. Please try again later."));

        var responseDto = job.ToDto();

        return AcceptedAtAction(nameof(GetExtractionStatus), new { id = job.Id }, responseDto);
    }

    [HttpGet("extract/jobs/{id:guid}")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetExtractionStatus(Guid id)
    {
        var job = _jobManager.GetJob(id);
        
        if (job == null)
            return NotFound(new ErrorDto("Job not found."));

        return Ok(job.ToDto());
    }

    [HttpPost("extract/jobs/{id:guid}/cancel")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CancelExtraction(Guid id)
    {
        var job = _jobManager.CancelJob(id);

        if (job == null)
            return NotFound(new ErrorDto("Job not found."));

        if (job.Status != ExtractionStatus.Cancelled)
            return Conflict(new ErrorDto("Completed jobs cannot be cancelled."));

        return Ok(job.ToDto());
    }

    [HttpPost("extract/jobs/{id:guid}/resume")]
    [ProducesResponseType(typeof(ExtractionJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult ResumeExtraction(Guid id)
    {
        var job = _jobManager.GetJob(id);
        
        if (job == null)
            return NotFound(new ErrorDto("Job not found."));

        if (job.Status != ExtractionStatus.Cancelled)
            return Conflict(new ErrorDto("Only a cancelled job can be resumed."));

        if (_jobManager.ResumeJob(id) == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorDto("Extraction queue is full. Please try again later."));

        return Ok(job.ToDto());
    }
}