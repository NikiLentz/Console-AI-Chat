namespace ConsoleAIChat.Database.Models;

public class ChatMessage
{
    public Guid Id { get; set; }
    public string Role { get; set; } // "user" or "assistant"
    public string Content { get; set; }
}