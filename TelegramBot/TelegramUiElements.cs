using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
    public static class TelegramUiElements
    {
        public const string EnterDatePrompt = "Напишите, какую дату в формате <b>дд.мм</b> или <b>дд.мм.гггг</b> (например, <b>25.06</b>):";
        public const string InvalidDatePrompt = "Неверный формат даты. Напишите, какую дату в формате <b>дд.мм</b> или <b>дд.мм.гггг</b> (например, <b>25.06</b>):";
        public const string DefaultPreferences = "Без предпочтений";
        public const string DefaultCommandsPrompt = "Доступные команды:\n/menu — показать меню на сегодня\n/plan_ai — спланировать меню через ИИ\n\nИли просто отправьте ссылку на видео-рецепт!";
        public const string SearchPrompt = "Напишите ингредиенты через запятую (например: курица, сыр, ананасы):";
        public const string SearchEmptyError = "Ошибка: вы не ввели ингредиенты для поиска.";

        public static string GetPreferencesPrompt(DateOnly date) =>
            $"Выбранная дата: <b>{date:dd.MM.yyyy}</b>.\n\nЕсть ли пожелания к меню (например, низкокалорийное, быстрое)? Напишите их сообщением или нажмите кнопку:";

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
