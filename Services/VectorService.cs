using Jint.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace ConsoleAIChat.Services;

public class VectorService
{
    private IConfiguration _configuration;
    private readonly ILogger<VectorService> _logger;
    private readonly  IEmbeddingGenerator<string,Embedding<float>> _embeddingGenerator;
    
    public VectorService(IConfiguration configuration, ILogger<VectorService> logger)
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
        
    }

    public async Task<float[]> GenerateDenseVectorAsync(string text)
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
    
    public async Task<SparseVector> GenerateSparseVectorAsync(string text)
    {
        throw new  NotImplementedException();
    }
    
    public async Task IngestTextAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var vector = await GenerateDenseVectorAsync(text);
            
            _logger.LogInformation("Generated vector for text: {Text}", text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting text: {Text}", text);
        }
    }
}