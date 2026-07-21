using Serilog;
using TelegramBot;
using TelegramBot.ApiClients;
using TelegramBot.Contracts;
using TelegramBot.Extensions;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddUserSecrets<Program>();

    builder.Services.AddSerilog();

    var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5000";

    builder.Services.AddHttpClient("RecipeScribeApi", client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
    });

    builder.Services.AddTransient<IRecipeApiClient>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient("RecipeScribeApi");
        var logger = sp.GetRequiredService<ILogger<RecipeApiClient>>();
        return new RecipeApiClient(http, logger);
    });

    builder.Services.AddTransient<IMealPlanApiClient>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient("RecipeScribeApi");
        var logger = sp.GetRequiredService<ILogger<MealPlanApiClient>>();
        return new MealPlanApiClient(http, logger);
    });

    builder.Services.AddTransient<ISubstitutionApiClient>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient("RecipeScribeApi");
        var logger = sp.GetRequiredService<ILogger<SubstitutionApiClient>>();
        return new SubstitutionApiClient(http, logger);
    });

    builder.Services.AddTelegramServices(builder.Configuration);
    builder.Services.AddHostedService<TelegramBotService>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
