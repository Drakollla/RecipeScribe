# RecipeScribe

Проект позволяет извлекать кулинарные рецепты из видео-источников.

---

## Поддерживаемые платформы для импорта

YouTube, Instagram, Pinterest.

---

## Ключевые возможности

*   **Импорт рецептов:** Бот последовательно пытается прочитать описание видео и закрепленный комментарий. Если текста нет, запускается локальное распознавание речи (Whisper.net), после чего LLM форматирует полученные данные в структурированный JSON (название, ингредиенты с объемами, упорядоченные шаги приготовления).
*   **Cписок покупок:** Система автоматически объединяет дублирующиеся ингредиенты для выбранного меню, суммирует их объемы, а затем с помощью LLM распределяет продукты по отделам супермаркета (овощи, мясо, бакалея, специи).
*   **Интерактивное планирование меню:** По команде `/plan_ai` бот предлагает выбрать день (сегодня, завтра или любую другую дату в ручном вводе) и с помощью LLM подбирает меню из базы данных (в доработке).

---

## Стек технологий

*   **Платформа:** .NET 8, EF Core 8 (SQL Server LocalDB / SQLite)
*   **AI-оркестрация:** Semantic Kernel, служба IChatCompletionService (совместима с OpenAI)
*   **Транскрибация:** Whisper.net (библиотека-обертка над whisper.cpp, модель ggml-base.bin)
*   **Загрузка медиа:** YoutubeDLSharp (обертка над yt-dlp)
*   **Интерфейс:** Telegram.Bot

---

## Настройка проекта

### 1. Конфигурация (appsettings.json)

Заполните основные параметры подключения к вашей модели LLM на уровне проекта ConsoleApp:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=RecipeScribeDb;Trusted_Connection=True;"
  },
  "LlmSettings": {
    "Endpoint": "https://api.groq.com/openai/v1/",
    "ModelId": "openai/gpt-oss-20b",
    "TargetLanguage": "Russian"
  },
  "ApiKeys": {
    "Llm": "",
    "Telegram": ""
  }
}
```

### 2. Хранение секретов (User Secrets)

Для локального использования пропишите API-ключи во внутреннее хранилище секретов .NET:

```dotnet user-secrets init --project ConsoleApp```

```dotnet user-secrets set "ApiKeys:Telegram" "ВАШ_ТОКЕН_ТГ_БОТА" --project ConsoleApp```

```dotnet user-secrets set "ApiKeys:Llm" "ВАШ_API_КЛЮЧ_ИИ" --project ConsoleApp```

# Запуск

Применение миграций базы данных:

```dotnet ef database update --project Infrastructure --startup-project ConsoleApp```

Запуск приложения:

```dotnet run --project ConsoleApp```
