using System.Collections.Concurrent;

namespace RecipeScribeApi.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _requests = new();
    private readonly TimeSpan _window = TimeSpan.FromSeconds(30);
    private readonly HashSet<string> _limitedPaths;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _limitedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/recipes/extract",
            "/api/mealplans/generate"
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/');

        if (path != null && _limitedPaths.Contains(path))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ip}:{path}";

            if (_requests.TryGetValue(key, out var lastCall))
            {
                if (DateTime.UtcNow - lastCall < _window)
                {
                    _logger.LogWarning("Rate limit hit for {Ip} on {Path}", ip, path);
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        $$"""{""error"":"Too many requests. Try again in {{_window.TotalSeconds}} seconds.","errorType":"RateLimited"}""");
                    return;
                }
            }

            _requests[key] = DateTime.UtcNow;
        }

        await _next(context);
    }
}
