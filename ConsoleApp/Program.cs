using ConsoleApp.Services;
using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TelegramBot;
using TelegramBot.Extensions;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Program>();
    })
    .ConfigureServices((hostContext, services) =>
    {
        var appMode = hostContext.Configuration["AppMode"] ?? "Both";

        if (appMode.Equals("Telegram", StringComparison.OrdinalIgnoreCase))
        {
            services.AddTelegramServices(hostContext.Configuration);
            services.AddHostedService<TelegramBotService>();
        }
        else if (appMode.Equals("Web", StringComparison.OrdinalIgnoreCase))
        {
            services.AddInfrastructureServices(hostContext.Configuration);
            services.AddHostedService<WebUiService>();
        }
        else
        {
            services.AddTelegramServices(hostContext.Configuration);
            services.AddHostedService<TelegramBotService>();
            services.AddHostedService<WebUiService>();
        }

        services.AddLogging(builder => builder.AddSerilog(dispose: true));
    })
    .Build();

await host.RunAsync();