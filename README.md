# RecipeScribe
Извлекает рецепты из YouTube-видео: описание → комментарий → транскрибация аудио (через Whisper + LLM).

## Запуск

```powershell
dotnet build RecipeScribe.sln
dotnet run --project RecipeScribe\ConsoleApp
```

## Настройка

```json
// ConsoleApp/appsettings.json
{
  "LlmSettings": {
    "Endpoint": "https://api.groq.com/openai/v1/",
    "ModelId": "openai/gpt-oss-20b",
    "TargetLanguage": "Russian"
  },
  "LLM:Provider": "OpenAI"
}
```

API-ключ — через User Secrets:

```powershell
dotnet user-secrets set "ApiKeys:Llm" "<ключ>" --project ConsoleApp
```

## Как это работает

1. **YouTubeDownloader** — скачивает аудио через yt-dlp, читает описание и первый комментарий
2. **WhisperTranscriber** — распознаёт речь из аудио (Whisper + ffmpeg)
3. **RecipeParser** — отправляет текст в LLM, получает JSON-рецепт
4. Если рецепт есть в описании — берёт оттуда, если нет — проверяет комментарий, в последнюю очередь транскрибирует аудио
