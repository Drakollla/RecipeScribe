using ConsoleApp;
using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

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
        services.AddInfrastructureServices(hostContext.Configuration);
        services.AddHostedService<TelegramBotService>();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
    })
    .Build();

await host.RunAsync();