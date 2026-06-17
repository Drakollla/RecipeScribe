using ConsoleApp;
using Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .Build();

var serviceProvider = new ServiceCollection()
    .AddInfrastructureServices(configuration)
    .AddLogging(builder => builder.AddSerilog(dispose: true))
    .AddTransient<ConsoleUi>()
    .BuildServiceProvider();

var app = serviceProvider.GetRequiredService<ConsoleUi>();
await app.RunAsync();