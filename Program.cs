// See https://aka.ms/new-console-template for more information

using ConsoleAIChat.Database;
using ConsoleAIChat.Services;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();


try
{
    var services = new ServiceCollection();

    services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));


    services.AddDbContextFactory<AppDbContext>(options =>
        options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

    services.AddSingleton<IAIChatService, AIChatService>();
    services.AddSingleton<IConfiguration>(configuration);
    var serviceProvider = services.BuildServiceProvider();

    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Application Starting");

    var aiChatService = serviceProvider.GetService<IAIChatService>();


    var connectionString = configuration.GetConnectionString("DefaultConnection");

    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
    optionsBuilder.UseNpgsql(connectionString);

    using var dbContext = new AppDbContext(optionsBuilder.Options);


    Console.WriteLine("Chat with the AI (type 'exit' to quit):");
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("You: ");
        Console.ResetColor();
        var input = Console.ReadLine();
        //print input

        Console.WriteLine();

        if (input?.ToLower() == "exit")
        {
            break;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("AI: ");
            Console.ResetColor();
            await foreach (var chunk in aiChatService.StreamChatCompletionAsync(input ?? string.Empty))
            {
                Console.Write(chunk);
            }

            Console.WriteLine();
        }

        Console.WriteLine();
    }
} catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}