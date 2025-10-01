using ConsoleAIChat.Database;
using ConsoleAIChat.Database.Models;
using ConsoleAIChat.Plugins;
using ConsoleAIChat.Services.Helper;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SharpToken;

namespace ConsoleAIChat.Services;

public class AIChatService :IAIChatService
{
    private readonly IChatCompletionService _reasoningChatCompletionService;
    private readonly IChatCompletionService _summarizationService;
    private readonly IChatCompletionService _routerService;
    private readonly AppDbContext _context;
    private readonly Kernel _kernel;
    private readonly ILogger<AIChatService> _logger;
    private readonly GptEncoding _encoding = GptEncoding.GetEncodingForModel("gpt-4o");

    private readonly OpenAIPromptExecutionSettings _openAIPromptExecutionSettings =
        new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions};

    public AIChatService(IDbContextFactory<AppDbContext> contextFactory, IConfiguration configuration, ILogger<AIChatService> logger, ILogger<SQLDatabasePlugin> dbLogger, ILogger<CodeInterpreterPlugin> codeLogger)
    {
        var builder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("gpt-4o", configuration["OpenAI:ApiKey"]);
        builder.Plugins.AddFromObject(new SQLDatabasePlugin(contextFactory, dbLogger));
        builder.Plugins.AddFromObject(new CodeInterpreterPlugin(codeLogger));
        _kernel = builder.Build();
        _reasoningChatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
        _context = contextFactory.CreateDbContext();
        _logger = logger;
        _summarizationService = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("gpt-4.1-nano", configuration["OpenAI:ApiKey"])
            .Build()
            .GetRequiredService<IChatCompletionService>();
       
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reducer = new TokenCountBasedReducer(_summarizationService);
        _logger.LogInformation("Received user message: {UserMessage}", userMessage);
        var history = await _context.ChatMessages.ToListAsync(cancellationToken)
            .ContinueWith(t => 
            {
                var chatHistory = new ChatHistory();
                foreach (var msg in t.Result)
                {
                    if (msg.Role == "user")
                    {
                        chatHistory.AddUserMessage(msg.Content);
                    } else if (msg.Role == "assistant")
                    {
                        chatHistory.AddAssistantMessage(msg.Content);
                    }
                }
                return chatHistory;
            }, cancellationToken);
        history.AddUserMessage(userMessage);
        _logger.LogInformation("Current chat history token count before reduction: {TokenCount}", history.Sum(m => _encoding.Encode(m.Content ?? "").Count));
        
        history = await reducer.ReduceAsync(history, cancellationToken);
        _logger.LogInformation("Chat history token count after reduction: {TokenCount}", history.Sum(m => _encoding.Encode(m.Content ?? "").Count));
        var fullAssistantMessage = string.Empty;
        await foreach (var update in StreamReasoningCompletionInternalAsync(history, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update))
            {
                fullAssistantMessage += update;
                yield return update;
            }
        }
        _context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            Role = "user",
            Content = userMessage
        });
        _context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            Role = "assistant",
            Content = fullAssistantMessage
        });
        await _context.SaveChangesAsync(cancellationToken);
    }
    
    private async IAsyncEnumerable<string> StreamReasoningCompletionInternalAsync(ChatHistory history, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _reasoningChatCompletionService.GetStreamingChatMessageContentsAsync(history,
                           executionSettings: _openAIPromptExecutionSettings,
                           kernel: _kernel,
                             cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Content))
            {
                yield return update.Content;
            }
        }
    }
    
    
    
}