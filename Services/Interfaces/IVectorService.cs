namespace ConsoleAIChat.Services.Interfaces;

public interface IVectorService
{
    public Task IngestTextAsync(string[] chunks, String filename, CancellationToken cancellationToken = default);

    public Task<Content[]> QuerySimilarChunksAsync(string query, int topK = 50, float scoreThreshold = 0.7f, CancellationToken cancellationToken = default);
}

public record Content(string Text, string Filename);