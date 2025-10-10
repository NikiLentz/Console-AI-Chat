using Anthropic.SDK.Constants;
using ConsoleAIChat.Database;
using ConsoleAIChat.Database.Models;
using ConsoleAIChat.Plugins;
using ConsoleAIChat.Services.Helper;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using SharpToken;

namespace ConsoleAIChat.Services;

public class AIChatService :IAIChatService
{
    private readonly IChatCompletionService _reasoningChatCompletionService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Kernel _kernel;
    private readonly ILogger<AIChatService> _logger;
    private readonly GptEncoding _encoding = GptEncoding.GetEncodingForModel("gpt-4o");
    private readonly TokenCountBasedReducer _reducer;
    private readonly string _systemPrompt;

    private readonly OpenAIPromptExecutionSettings _openAIPromptExecutionSettings =
        new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions};
    
    private readonly PromptExecutionSettings _promptExecutionSettings = new OpenAIPromptExecutionSettings
    {
        ModelId = AnthropicModels.Claude45Sonnet, 
        MaxTokens = 2048,
        Temperature = 0.7,
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    };


    public AIChatService(IDbContextFactory<AppDbContext> contextFactory, 
        IConfiguration configuration, 
        ILogger<AIChatService> logger,
        IVectorService vectorService,
        TokenCountBasedReducer reducer,
        [FromKeyedServices("ToolKernel")] Kernel pluginKernel)
    {
        _kernel = pluginKernel;
        _reasoningChatCompletionService = _kernel.GetRequiredService<IChatCompletionService>() 
                                         ?? throw new ArgumentNullException("Reasoning Chat Completion Service not found in Plugin Kernel");
        _contextFactory = contextFactory;
        _logger = logger;
        _reducer = reducer;
        _systemPrompt = configuration["AI:StarterPrompt"] ?? throw new ArgumentNullException("System prompt not configured");
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var history = new ChatHistory();
        
        _logger.LogInformation("Received user message: {UserMessage}", userMessage);
        history = await context.ChatMessages.ToListAsync(cancellationToken)
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
        
        history = await _reducer.ReduceAsync(history, cancellationToken);
        
        history.AddSystemMessage(_systemPrompt);
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
        context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            Role = "user",
            Content = userMessage
        });
        context.ChatMessages.Add(new ChatMessage
        {
            Id = Guid.NewGuid(),
            Role = "assistant",
            Content = fullAssistantMessage
        });
        await context.SaveChangesAsync(cancellationToken);
    }
    
    private async IAsyncEnumerable<string> StreamReasoningCompletionInternalAsync(ChatHistory history, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _reasoningChatCompletionService.GetStreamingChatMessageContentsAsync(history,
                           executionSettings: _promptExecutionSettings,
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