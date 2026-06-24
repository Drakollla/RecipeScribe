using Telegram.Bot.Types.ReplyMarkups;

namespace Infrastructure.Helpers
{
    public static class TelegramUiElements
    {
        public const string EnterDatePrompt = "Пожалуйста, введите дату в формате *ДД.ММ* или *ДД.ММ.ГГГГ* (например, *25.06*):";
        public const string InvalidDatePrompt = "Неверный формат даты. Пожалуйста, введите дату в формате *ДД.ММ* или *ДД.ММ.ГГГГ* (например, *25.06*):";
        public const string DefaultPreferences = "без особых предпочтений";
        public const string DefaultCommandsPrompt = "Доступные команды:\n/menu — показать меню на сегодня\n/plan_ai — спланировать меню через ИИ\n\nИли просто отправьте ссылку на видео-рецепт!";

        public static string GetPreferencesPrompt(DateOnly date) =>
            $"Выбрана дата: *{date:dd.MM.yyyy}*.\n\nЕсть ли пожелания к меню (например, вегетарианское, быстрое)? Напишите их сообщением ниже или нажмите кнопку:";

        public static InlineKeyboardMarkup GetDateSelectionKeyboard() =>
            new InlineKeyboardMarkup(
            [
                [
                     InlineKeyboardButton.WithCallbackData("Сегодня", "plan_date:today"),
                     InlineKeyboardButton.WithCallbackData("Завтра", "plan_date:tomorrow")
                ],
                [
                     InlineKeyboardButton.WithCallbackData("Другая дата", "plan_date:custom")
                ]
            ]);

        public static InlineKeyboardMarkup GetPreferencesKeyboard() =>
            new InlineKeyboardMarkup(
            [
                [InlineKeyboardButton.WithCallbackData("Без предпочтений", "pref_none")]
            ]);
    }
}
