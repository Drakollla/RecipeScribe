using System.Net;
using System.Text.Json;
using Core.Enums;
using Core.Exceptions;
using Shared.DTOs;

namespace RecipeScribeApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errorMessage, errorType) = exception switch
        {
            NotFoundException => (HttpStatusCode.NotFound, exception.Message, null),
            BadRequestException => (HttpStatusCode.BadRequest, exception.Message, null),
            RecipeScribeException rse => rse.Type switch
            {
                ErrorType.VideoNotFound => (HttpStatusCode.NotFound, rse.Message, rse.Type.ToString()),
                ErrorType.Network => (HttpStatusCode.BadGateway, rse.Message, rse.Type.ToString()),
                ErrorType.TranscriptionFailed => (HttpStatusCode.BadGateway, rse.Message, rse.Type.ToString()),
                ErrorType.LlmFailure => (HttpStatusCode.BadGateway, rse.Message, rse.Type.ToString()),
                ErrorType.ParseError => (HttpStatusCode.UnprocessableEntity, rse.Message, rse.Type.ToString()),
                _ => (HttpStatusCode.InternalServerError, rse.Message, rse.Type.ToString())
            },
            _ => (HttpStatusCode.InternalServerError, "Internal server error.", null)
        };

        if (statusCode == HttpStatusCode.InternalServerError && errorType == null)
            _logger.LogError(exception, "Unhandled exception");
        else
            _logger.LogWarning(exception, "Handled exception [{ErrorType}]: {Message}", errorType ?? "N/A", exception.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var result = JsonSerializer.Serialize(new ErrorDto(errorMessage, ErrorType: errorType));
        await context.Response.WriteAsync(result);
    }
}
