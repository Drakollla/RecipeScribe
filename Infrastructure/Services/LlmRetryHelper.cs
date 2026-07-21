using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Infrastructure.Services
{
    internal static class LlmRetryHelper
    {
        private const int MaxRetries = 5;

        public static async Task<string> CallWithRetryAsync(
            Kernel kernel,
            string prompt,
            PromptExecutionSettings? settings = null,
            ILogger? logger = null,
            string? logPrefix = null,
            CancellationToken ct = default)
        {
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage(prompt);

            var prefix = logPrefix ?? "LLM";

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var response = await chatService.GetChatMessageContentAsync(history, settings, kernel, ct);
                    return response.Content ?? string.Empty;
                }
                catch (HttpRequestException ex) when (IsClientError(ex))
                {
                    logger?.LogError(ex, "[{Prefix}] Неисправимая ошибка HTTP, прекращаю попытки", prefix);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[{Prefix}] Попытка {Attempt}/{MaxRetries} завершилась ошибкой", prefix, attempt, MaxRetries);

                    if (attempt == MaxRetries)
                        throw;

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }

            throw new InvalidOperationException("Unreachable");
        }

        public static async Task<T> CallWithRetryAsync<T>(
            Func<Task<T>> operation,
            Func<T, bool>? validateResult = null,
            ILogger? logger = null,
            string? logPrefix = null,
            CancellationToken ct = default)
        {
            var prefix = logPrefix ?? "LLM";

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var result = await operation();

                    if (validateResult == null || validateResult(result))
                        return result;
                }
                catch (HttpRequestException ex) when (IsClientError(ex))
                {
                    logger?.LogError(ex, "[{Prefix}] Неисправимая ошибка HTTP, прекращаю попытки", prefix);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[{Prefix}] Попытка {Attempt}/{MaxRetries} завершилась ошибкой", prefix, attempt, MaxRetries);

                    if (attempt == MaxRetries)
                        throw;

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }

            throw new InvalidOperationException("Unreachable");
        }

        private static bool IsClientError(HttpRequestException ex) =>
            ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 400 && (int)ex.StatusCode.Value < 500;
    }
}
