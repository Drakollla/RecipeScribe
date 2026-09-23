namespace Infrastructure.Services;

public static class ExtractionJobMessages
{
    public const string LogStarted = "ExtractionWorker запущен и ожидает задачи.";
    public const string LogJobStarted = "Начало обработки задачи {JobId} для URL: {Url}";
    public const string LogJobCompleted = "Задача {JobId} успешно завершена. Создано рецептов: {Count} (ID: [{RecipeIds}])";
    public const string LogJobInterrupted = "Задача {JobId} отменена (пользователем или при остановке приложения).";
    public const string LogJobFailed = "Ошибка при обработке задачи {JobId}";

    public const string MessageProcessing = "Скачивание и обработка видео...";
    public const string MessageCompleted = "Готово!";
    public const string MessageShutdown = "Сервер перезагружается.";
}