namespace ConsoleAIChat.Services.Interfaces;

public interface IFileIngestionService
{
    public Task IngestFilesAsync(CancellationToken cancellationToken = default);
}