using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Infrastructure.Services;

public class DynamicChatCompletionService : IChatCompletionService
{
    private readonly IConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SocketsHttpHandler _handler;
    private readonly object _lock = new();

    private OpenAIChatCompletionService? _inner;
    private string? _currentKey;

    public DynamicChatCompletionService(IConfiguration config, ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
        _handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) };
    }

    public IReadOnlyDictionary<string, object?> Attributes => GetInner().Attributes;

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => GetInner().GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => GetInner().GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

    private OpenAIChatCompletionService GetInner()
    {
        var endpoint = _config["LlmSettings:Endpoint"] ?? string.Empty;
        var modelId = _config["LlmSettings:ModelId"] ?? string.Empty;
        var apiKey = _config["ApiKeys:Llm"] ?? "ollama";

        var key = $"{endpoint}|{modelId}";

        lock (_lock)
        {
            if (_inner != null && _currentKey == key)
                return _inner;

            var httpClient = new HttpClient(_handler)
            {
                BaseAddress = string.IsNullOrWhiteSpace(endpoint) ? null : new Uri(endpoint),
                Timeout = TimeSpan.FromMinutes(5)
            };

            var inner = new OpenAIChatCompletionService(
                modelId: modelId,
                apiKey: apiKey,
                organization: null,
                httpClient: httpClient,
                loggerFactory: _loggerFactory);

            _inner = inner;
            _currentKey = key;

            return inner;
        }
    }
}
