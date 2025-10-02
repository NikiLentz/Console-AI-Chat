// See https://aka.ms/new-console-template for more information

using ConsoleAIChat.Database;
using ConsoleAIChat.Plugins;
using ConsoleAIChat.Services;
using ConsoleAIChat.Services.Helper;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Serilog;

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.development.json", optional: false, reloadOnChange: true)
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
    services.AddSingleton<IFileIngestionService, FileIngestionService>();
    services.AddSingleton<IVectorService, VectorService>();
    services.AddSingleton<TokenCountBasedReducer>();
    services.AddKeyedTransient<Kernel>("ToolKernel", (sp, key) =>
    {
        var builder = Kernel.CreateBuilder();
        // TODO log the kernel calls to a different file
        // builder.Services.AddSingleton(sp.GetRequiredService<ILoggerFactory>());
        builder.Services.AddSingleton(sp.GetRequiredService<ILogger<VectorSearchPlugin>>());
        builder.Services.AddSingleton(sp.GetRequiredService<ILogger<SQLDatabasePlugin>>());
        builder.Services.AddSingleton(sp.GetRequiredService<ILogger<CodeInterpreterPlugin>>());
        builder.Services.AddSingleton(sp.GetRequiredService<IVectorService>());
        builder.Services.AddSingleton(sp.GetRequiredService<IDbContextFactory<AppDbContext>>());
        builder.AddOpenAIChatCompletion("gpt-5", configuration["OpenAI:ApiKey"]);
        // // builder.AddGoogleAIGeminiChatCompletion("gemini-2.5-pro", configuration["Gemini:ApiKey"]);
        // builder.AddOpenAIChatCompletion("gemini-2.5-pro", new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"), configuration["Gemini:ApiKey"]);
        builder.Plugins.AddFromType<VectorSearchPlugin>();
        builder.Plugins.AddFromType<SQLDatabasePlugin>();
        builder.Plugins.AddFromType<CodeInterpreterPlugin>();
        return builder.Build();
    });
    
    services.AddKeyedTransient<Kernel>("BaseKernel", (sp, key) => 
        Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("gpt-4.1-nano", configuration["OpenAI:ApiKey"])
            .Build());
 
    
    var serviceProvider = services.BuildServiceProvider();

    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Application Starting");

    var aiChatService = serviceProvider.GetService<IAIChatService>();


    var connectionString = configuration.GetConnectionString("DefaultConnection");

    var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
    optionsBuilder.UseNpgsql(connectionString);

    using var dbContext = new AppDbContext(optionsBuilder.Options);
    var ingestionService = serviceProvider.GetService<IFileIngestionService>();
    if (ingestionService != null)
    {
        //start ingesting files
        await ingestionService.IngestFilesAsync();
    }

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