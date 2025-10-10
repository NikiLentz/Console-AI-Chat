using ConsoleAIChat.Database;
using ConsoleAIChat.Database.Models;
using ConsoleAIChat.Services.Interfaces;
using Jint.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ConsoleAIChat.Services;

public class VectorService : IVectorService
{
    private IConfiguration _configuration;
    private readonly ILogger<VectorService> _logger;
    private readonly  IEmbeddingGenerator<string,Embedding<float>> _embeddingGenerator;
    private QdrantClient _qdrantClient;
    private IDbContextFactory<AppDbContext> _contextFactory;
    
    public VectorService(IConfiguration configuration, ILogger<VectorService> logger, IDbContextFactory<AppDbContext> contextFactory)
    {
        _configuration = configuration;
        _logger = logger;
        var apiKey = _configuration["OpenAI:ApiKey"] ?? 
                     throw new ArgumentNullException("OpenAI API Key not set");

        IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
        
        #pragma warning disable SKEXP0010
        kernelBuilder.AddOpenAIEmbeddingGenerator(
            modelId: "text-embedding-3-small",          
            apiKey: apiKey ,
            dimensions: 1536
        );
        var kernel = kernelBuilder.Build();
        _embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _qdrantClient = new QdrantClient("localhost", port:6334);
        _contextFactory = contextFactory;
        
    }

    private async Task<float[]> GenerateDenseVectorAsync(string text)
    {
        try
        {
            var embedding = await _embeddingGenerator.GenerateAsync(text);
            return embedding.Vector.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating dense vector for text: {Text}", text);
            throw;
        }
        
    }
    
    private async Task<SparseVector> GenerateSparseVectorAsync(string text)
    {
        throw new  NotImplementedException();
    }
    
    public async Task IngestTextAsync(string[] chunks, string filename, CancellationToken cancellationToken = default)
    {
        try
        {
            var points = new List<PointStruct>();
        
            for (int i = 0; i < chunks.Length; i++)
            {
                var vector = await GenerateDenseVectorAsync(chunks[i]);
            
                var point = new PointStruct
                {
                    Id = Guid.NewGuid(),
                    Vectors = vector, // Each chunk gets its own point
                    Payload =
                    {
                        ["filename"] = filename,
                        ["chunk_index"] = i,
                        ["chunk_text"] = chunks[i], // Store original text for retrieval
                        ["total_chunks"] = chunks.Length
                    }
                };
            
                points.Add(point);
            }
        
            await _qdrantClient.UpsertAsync("my-collection", points, cancellationToken: cancellationToken);
            _logger.LogInformation("Successfully ingested {ChunkCount} chunks from file: {Filename}", chunks.Length, filename);
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            context.VectorDatabaseFiles.Add(new VectorDatabaseFile
            {
                Id = Guid.NewGuid(),
                FileName = filename,
                FilePath = filename
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting text from file: {Filename}", filename);
        }
    }
    
    public async Task<Content[]> QuerySimilarChunksAsync(
        string queryText, 
        int topK = 5, 
        float scoreThreshold = 0.8f,
        CancellationToken cancellationToken = default)
    {
        var queryVector = await GenerateDenseVectorAsync(queryText);
    
        var searchResult = await _qdrantClient.SearchAsync(
            collectionName: "my-collection",
            vector: queryVector,
            limit: (ulong)topK, 
            cancellationToken: cancellationToken);
    
        var similarChunks = searchResult
            .Select(hit => new Content(
                Text: hit.Payload["chunk_text"].StringValue,  // Use StringValue for safety
                Filename: hit.Payload["filename"].StringValue
            ))
            .ToArray();
    
        return similarChunks;  // Already limited by Qdrant
    }

    
}