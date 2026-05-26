using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Infrastructure
{
    public class LlmService
    {
        private readonly Kernel _kernel;

        public LlmService(Kernel kernel)
        {
            _kernel = kernel;
        }

        public async Task<string> InitialChatAsync(string prompt)
        {
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddUserMessage(prompt);

            var response = await chatService.GetChatMessageContentAsync(history, null, _kernel);
            return response.Content ?? string.Empty;
        }
    }
}