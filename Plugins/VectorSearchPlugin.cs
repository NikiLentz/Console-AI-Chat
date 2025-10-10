using System.ComponentModel;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace ConsoleAIChat.Plugins;

public class VectorSearchPlugin(ILogger<VectorSearchPlugin> logger, IVectorService vectorService)
{
    [KernelFunction, Description("Search for relevant documents based on query. Use when you need information from stored documents. Search in German first, then English if needed. State clearly if information cannot be found.")]  public async Task<string> SearchInVectorDbAsync([Description("The text for which to search for similar chunks in the vectordb")]string query)
    {
        try{
            logger.LogInformation("Searching in vector DB for query: {Query}", query);
            var results = await vectorService.QuerySimilarChunksAsync(query, topK:5);
            var json = JsonConvert.SerializeObject(results);
            return json;
        } catch(Exception ex)
        {
            logger.LogError(ex, "Error logging query: {Query}", query);
            return $"Error: {ex.Message}";
        }
        
    }
    
}