using Core.Enums;
using Core.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBot.Strategies
{
    public class AiPreferencesMessageCommand : IMessageCommand
    {
        private readonly TelegramMealPlanFlow _mealPlanFlow;

        public AiPreferencesMessageCommand(TelegramMealPlanFlow mealPlanFlow)
        {
            _mealPlanFlow = mealPlanFlow;
        }

        public bool CanHandle(string text, BotState state) => state == BotState.WaitingForAiPreferences;

        public async Task ExecuteAsync(ITelegramBotClient botClient, Message message, UserStateInfo userStateinfo, CancellationToken cancellationToken)
        {
            userStateinfo.State = BotState.None;
            string text = message.Text?.Trim() ?? string.Empty;
            userStateinfo.LastPreferences = text;

            await _mealPlanFlow.ProcessAiPlanningAsync(botClient, message.Chat.Id, userStateinfo.TargetDate, text, cancellationToken);
        }
    }
}