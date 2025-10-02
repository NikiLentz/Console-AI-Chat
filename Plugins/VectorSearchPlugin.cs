using System.ComponentModel;
using ConsoleAIChat.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace ConsoleAIChat.Plugins;

public class VectorSearchPlugin(ILogger<VectorSearchPlugin> logger, IVectorService vectorService)
{
    [KernelFunction, Description("Search for relevant documents based on the query Useful for when you need to find relevant documents to answer a question. Always use this function before answering a question. Search in english and again in german if the information isn't there. Don't make up information if you can't find relevant documents. Always search multiple times. Information for which files have been ingested can be found in the sql database")]
    public async Task<string> SearchInVectorDbAsync([Description("The text for which to search for similar chunks in the vectordb")]string query)
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