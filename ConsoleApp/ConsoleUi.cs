//using Core.Contracts;
//using Core.Enums;
//using Core.Exceptions;
//using Core.Models;
//using Microsoft.Extensions.DependencyInjection;
//using RecipeScribe.Infrastructure.Database;

//namespace ConsoleApp
//{
//    public class ConsoleUi
//    {
//        private readonly IVideoDownloader _downloader;
//        private readonly ITranscriber _transcriber;
//        private readonly IRecipeParser _parser;
//        private readonly IServiceProvider _serviceProvider;
//        private readonly RecipeRepository _repository;

//        public ConsoleUi(IVideoDownloader downloader,
//            ITranscriber transcriber,
//            IRecipeParser parser,
//            IServiceProvider serviceProvider,
//            RecipeRepository repository)
//        {
//            _downloader = downloader;
//            _transcriber = transcriber;
//            _parser = parser;
//            _serviceProvider = serviceProvider;
//            _repository = repository;
//        }

//        public async Task RunAsync()
//        {
//            while (true)
//            {
//                Console.WriteLine("\n=== ГЛАВНОЕ МЕНЮ ===");
//                Console.WriteLine("1. Распознать новый рецепт по ссылке");
//                Console.WriteLine("2. Поиск по ингредиентам");
//                Console.WriteLine("3. Выход");
//                Console.Write("Выберите действие: ");
//                string? choice = Console.ReadLine()?.Trim();

//                if (choice == "3" || string.IsNullOrWhiteSpace(choice))
//                {
//                    Console.WriteLine("До свидания!");
//                    return;
//                }

//                if (choice == "1")
//                {
//                    Console.Write("\nВведите ссылку с рецептом (или оставьте пустым для выхода): ");
//                    string? url = Console.ReadLine()?.Trim();

//                    if (string.IsNullOrWhiteSpace(url))
//                    {
//                        Console.WriteLine("До свидания!");
//                        return;
//                    }

//                    try
//                    {
//                        Recipe? recipe = await ProcessRecipePipelineAsync(url);

//                        if (recipe == null)
//                        {
//                            Console.WriteLine("\nОшибка: Не удалось распознать рецепт ни одним из способов.");
//                            continue;
//                        }

//                        Console.WriteLine("Сохраняю рецепт в базу данных...");
//                        await _repository.SaveRecipeAsync(recipe);

//                        PrintRecipeToConsole(recipe);

//                        await HandleExportOptionAsync(recipe);
//                    }
//                    catch (RecipeScribeException ex)
//                    {
//                        HandleRecipeScribeException(ex);
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"\nНеожиданная ошибка: {ex.Message}");
//                    }
//                }
//                else if (choice == "2")
//                {
//                    await HandleSearchByIngredientsAsync();
//                }
//            }
//        }

//        private async Task<Recipe?> ProcessRecipePipelineAsync(string url)
//        {
//            Console.WriteLine("Начинаю загрузку видео...");
//            var metadata = await _downloader.DownloadAudioAsync(url);

//            Console.WriteLine($"\nВидео успешно загружено.");
//            Console.WriteLine($"Название: {metadata.Title}");

//            Recipe? recipe = null;

//            if (!string.IsNullOrWhiteSpace(metadata.Description) && metadata.Description.Length > 100)
//            {
//                Console.WriteLine("\n[Быстрый путь 1] Пробую распарсить рецепт из описания...");
//                recipe = await _parser.ParseRecipeAsync(metadata.Description);
//            }

//            if (recipe == null || recipe.Ingredients.Count == 0 || recipe.Title == "Нет рецепта" || recipe.Title == "Ошибка парсинга JSON")
//            {
//                Console.WriteLine("\n[Быстрый путь 2] Проверяю закрепленный комментарий...");
//                string? firstComment = await _downloader.GetFirstCommentAsync(url);

//                if (!string.IsNullOrWhiteSpace(firstComment))
//                {
//                    Console.WriteLine("Нашелся комментарий! Пробую распарсить...");
//                    recipe = await _parser.ParseRecipeAsync(firstComment);
//                }
//            }

//            if (recipe == null || recipe.Ingredients.Count == 0 || recipe.Title == "Нет рецепта" || recipe.Title == "Ошибка парсинга JSON")
//            {
//                Console.WriteLine("\n[Медленный путь] Текст не найден. Запускаю транскрибацию...");
//                string transcript = await _transcriber.TranscribeAsync(metadata.AudioFilePath);

//                recipe = await _parser.ParseRecipeAsync(transcript);
//            }

//            return recipe;
//        }

//        private void PrintRecipeToConsole(Recipe recipe)
//        {
//            Console.WriteLine($"РЕЦЕПТ: {recipe.Title.ToUpper()}");
//            Console.WriteLine("\nИНГРЕДИЕНТЫ:");

//            foreach (var ingredient in recipe.Ingredients)
//            {
//                string amountText = string.IsNullOrWhiteSpace(ingredient.Amount) ? "" : $" — {ingredient.Amount}";
//                Console.WriteLine($"  • {ingredient.Name}{amountText}");
//            }

//            Console.WriteLine("\nШАГИ ПРИГОТОВЛЕНИЯ:");
            
//            foreach (var step in recipe.Steps)
//            {
//                Console.WriteLine($"  {step.Number}. {step.Description}");
//            }
//        }

//        private async Task HandleExportOptionAsync(Recipe recipe)
//        {
//            Console.Write("Сохранить этот рецепт в файл Markdown? (y/n): ");
//            string? answer = Console.ReadLine()?.Trim().ToLower();

//            if (answer != "y" && answer != "yes")
//                return;

//            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
//            string exportFolder = Path.Combine(myDocuments, "RecipeScribe", "SavedRecipes");
//            Directory.CreateDirectory(exportFolder);

//            char[] invalidChars = Path.GetInvalidFileNameChars();
//            string safeTitle = string.Join("_", recipe.Title.Split(invalidChars)).Trim();

//            if (string.IsNullOrWhiteSpace(safeTitle))
//            {
//                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
//                safeTitle = $"recipe_{timestamp}";
//            }

//            string savePath = Path.Combine(exportFolder, $"{safeTitle}.md");

//            var mdExporter = _serviceProvider.GetServices<IRecipeExporter>()
//                .FirstOrDefault(e => e.Format == "md");

//            if (mdExporter != null)
//            {
//                Console.WriteLine("Сохраняю рецепт в файл...");
//                await mdExporter.ExportAsync(recipe, savePath);
//                Console.WriteLine($"Рецепт успешно сохранен по адресу:\n{savePath}\n");
//            }
//            else
//            {
//                Console.WriteLine("Ошибка: Экспортер формата Markdown не зарегистрирован в системе.");
//            }
//        }

//        private void HandleRecipeScribeException(RecipeScribeException ex)
//        {
//            string msg = ex.Type switch
//            {
//                ErrorType.Network => "Нет соединения или видео недоступно",
//                ErrorType.VideoNotFound => "Видео не найдено или недоступно",
//                ErrorType.LlmFailure => "Не удалось распарсить рецепт (ошибка ИИ)",
//                ErrorType.ParseError => "Ответ от ИИ не содержит корректный рецепт",
//                ErrorType.TranscriptionFailed => "Не удалось распознать аудио",
//                _ => "Неизвестная ошибка"
//            };
//            Console.WriteLine($"\nОшибка: {msg}");
//            Console.WriteLine($"Детали: {ex.Message}");
//        }

//        private async Task HandleSearchByIngredientsAsync()
//        {
//            Console.Write("\nВведите ингредиенты через запятую (например: курица, сыр): ");
//            string? input = Console.ReadLine();

//            if (string.IsNullOrWhiteSpace(input))
//            {
//                Console.WriteLine("Ошибка: Вы не ввели ингредиенты для поиска.");
//                return;
//            }

//            var products = input.Split(',')
//                .Select(p => p.Trim())
//                .Where(p => !string.IsNullOrWhiteSpace(p))
//                .ToList();

//            if (!products.Any())
//            {
//                Console.WriteLine("Ошибка: Список ингредиентов пуст.");
//                return;
//            }

//            Console.WriteLine("Ищу подходящие рецепты в базе данных...");
//            var matchingRecipes = await _repository.SearchByIngredientsAsync(products);

//            if (!matchingRecipes.Any())
//            {
//                Console.WriteLine("Подходящих рецептов в базе данных не найдено.");
//                return;
//            }

//            Console.WriteLine($"\nНайдено подходящих рецептов: {matchingRecipes.Count}");
            
//            for (int i = 0; i < matchingRecipes.Count; i++)
//                Console.WriteLine($"  {i + 1}. {matchingRecipes[i].Title}");

//            Console.Write("\nВведите номер рецепта для детального просмотра (или Enter для пропуска): ");
//            string? selectChoice = Console.ReadLine()?.Trim();

//            if (int.TryParse(selectChoice, out int index) && index > 0 && index <= matchingRecipes.Count)
//                PrintRecipeToConsole(matchingRecipes[index - 1]);
//        }
//    }
//}