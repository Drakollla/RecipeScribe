using Core.Enums;
using Core.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Contracts;

namespace TelegramBot.Strategies;

public class SettingsValueCommand : IMessageCommand
{
    private readonly IUserApiClient _userApi;
    private readonly ILogger<SettingsValueCommand> _logger;

    public SettingsValueCommand(IUserApiClient userApi, ILogger<SettingsValueCommand> logger)
    {
        _userApi = userApi;
        _logger = logger;
    }

    public bool CanHandle(string text, BotState state) => state == BotState.WaitingForSettings;

    public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo state, CancellationToken cancellationToken)
    {
        var input = message.Text?.Trim() ?? string.Empty;

        if (!int.TryParse(input, out var servings) || servings < 1 || servings > 20)
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Пожалуйста, введите число от 1 до 20.",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );
            return;
        }

        state.State = BotState.None;

        try
        {
            var saved = await _userApi.UpdateSettingsAsync(message.Chat.Id, servings, cancellationToken);
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"Порции по умолчанию: {saved}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating settings for {ChatId}", message.Chat.Id);
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"Ошибка: {ex.Message}",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );
        }
    }
}
