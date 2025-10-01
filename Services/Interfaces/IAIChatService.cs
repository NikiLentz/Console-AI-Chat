namespace ConsoleAIChat.Services.Interfaces;

public interface IAIChatService
{
    IAsyncEnumerable<string> StreamChatCompletionAsync(string userMessage, CancellationToken cancellationToken = default);
}