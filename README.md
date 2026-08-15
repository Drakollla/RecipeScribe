# RecipeScribe

Автоматическое извлечение рецептов из YouTube (описание → закреплённый комментарий → транскрибация Whisper → структурирование LLM). Основной (и рекомендуется) интерфейс — Web UI.

## Быстрый старт (Web UI)

Достаточно одного API-процесса:
```powershell
dotnet build RecipeScribe.sln
dotnet run --project RecipeScribe\RecipeScribeApi
```

Откройте http://localhost:5074/.

## База данных (миграции SQLite)

Команды EF выполняются **из каталога `RecipeScribeApi`**:

```powershell
cd RecipeScribe\RecipeScribeApi
dotnet ef database update --project ..\Infrastructure --startup-project .
```

Если `dotnet-ef` (8.0.28) не установлен: `dotnet tool restore` из корня решения.

## Конфигурация

Переименуйте `RecipeScribeApi/appsettings.example.json` в `RecipeScribeApi/appsettings.json`. Ключи в - `dotnet user-secrets`:

```powershell
dotnet user-secrets set "ApiKeys:Llm" "<llm-key>"
```

## Справочник по конфигу (appsettings.json)

```jsonc
{
  // Copy this file to appsettings.json and fill in your values.
  // Real keys should go to dotnet user-secrets instead of the tracked file:
  //   dotnet user-secrets set "ApiKeys:Llm" "<llm-key>"
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=RecipeScribe.db"
  },
  "ApiKeys": {
    "Llm": "",
    "Telegram": ""
  },
  "LlmSettings": {
    // Any OpenAI-compatible endpoint. Examples:
    //   Ollama: http://localhost:11434/v1
    "Endpoint": "",
    // IMPORTANT: the recipe parser asks the model to return a JSON array with
    // ALL recipes found in one video. Models with a small output budget will
    // truncate the response and drop recipes. Use one with a large output
    // limit (verified: gemma4:31b-cloud on Ollama).
    "ModelId": "",
    // Language for recipes, scaling and substitutions.
    "TargetLanguage": "Russian"
  }
}
```

Файл может содержать `//`-комментарии — и `AddJsonFile`, и переключение профилей их допускают. Профили LLM (эндпоинт + modelId) хранятся отдельными JSON-файлами и управляются из вкладки «Настройки» Web UI — активный профиль записывается в `LlmSettings` и применяется без перезапуска.

## Архитектура

| Проект             | Роль                                                                                                                |
| :----------------- | :------------------------------------------------------------------------------------------------------------------ |
| **RecipeScribeApi** | ASP.NET Web API + одностраничный Web UI (`wwwroot`). Контроллеры, middleware (rate limiting, обработка ошибок), Serilog. |
| **Infrastructure** | Интеграции: LLM (Groq / OpenAI / Ollama, retry-логика), Whisper, `yt-dlp`, EF Core + SQLite, миграции.                |
| **Shared**         | DTO — формат обмена между API и Web UI.                                                                             |
| **Core**           | Доменная область: сущности EF, контракты, перечисления, исключения (без внешних зависимостей).                      |


## Как работает экстракция рецептов

1. **Описание видео** — если содержит текст длиннее 100 символов, отправляется в LLM для структурирования.
2. **Закреплённый комментарий** — если описания нет, сканируются закреплённые комментарии автора.
3. **Аудио-транскрибация (Whisper)** — если текста нет вообще: `yt-dlp` скачивает аудиодорожку → Whisper распознаёт речь → LLM собирает пошаговый рецепт.

Инструменты (Whisper, yt-dlp, ffmpeg) скачиваются автоматически при первом извлечении в папку `Core/Tools/`.
