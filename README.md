# RecipeScribe

Проект предназначен для извлечения рецептов из YouTube (Instagram, Pinterest) и экспорта в *.md формат, а также экспорта списков покупок.

## Быстрый старт

```powershell
dotnet build RecipeScribe.sln
```

Два терминала:

```powershell
# Терминал 1 — API (порт 5074)
dotnet run --project RecipeScribe\RecipeScribeApi

# Терминал 2 — Telegram бот
dotnet run --project RecipeScribe\TelegramBot
```

## Настройка

Секреты (общий `UserSecretsId`):

```powershell
dotnet user-secrets set "ApiKeys:Telegram" "<token>"
dotnet user-secrets set "ApiKeys:Llm" "<llm-key>"
```

`Api:BaseUrl` в `TelegramBot/appsettings.json` — должен указывать на API (по умолчанию `http://localhost:5074`).

## Миграции

```powershell
dotnet ef database update --project RecipeScribe\Infrastructure --startup-project RecipeScribe\RecipeScribeApi
```

## Архитектура

| Проект | Роль |
|---|---|
| `RecipeScribeApi` | ASP.NET Web API (контроллеры, Serilog, global exception middleware) |
| `TelegramBot` | Worker + Telegram (стратегии команд/колбэков, typed HttpClient → API) |
| `Infrastructure` | LLM (Semantic Kernel), Whisper, yt-dlp, EF Core + SQLite |
| `Shared` | DTO для обмена между API и ботом |
| `Core` | Domain-модели, контракты, enums (zero dependencies) |

## Как работает экстракция

1. **Описание видео** — LLM парсит описание, если оно длиннее 100 символов
2. **Комментарий** — если в описании нет рецепта, проверяется закреплённый комментарий
3. **Транскрибация** — если текста нигде нет, скачивается аудио → Whisper → LLM
