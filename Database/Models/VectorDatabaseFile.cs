namespace ConsoleAIChat.Database.Models;

public class VectorDatabaseFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}