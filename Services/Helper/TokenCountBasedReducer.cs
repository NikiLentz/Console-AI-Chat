using System.Text;
using Microsoft.SemanticKernel.ChatCompletion;
using SharpToken;

namespace ConsoleAIChat.Services.Helper;

public class TokenCountBasedReducer(
    IChatCompletionService summarizationService,
    int maxTokensSummaryModel = 100000,
    int maxTokens = 100000,
    string summarizationPrompt =
        "Summarize the following conversation between a user and an AI assistant. Focus on key points, decisions, and action items. Be concise but comprehensive. Avoid including trivial details.") 
{
    private readonly GptEncoding _encoding = GptEncoding.GetEncodingForModel("gpt-4o");
    
    

    public async Task<ChatHistory> ReduceAsync(ChatHistory chatHistory, 
        CancellationToken cancellationToken = new CancellationToken())
    {
        var messageList = chatHistory;
        var totalTokens = messageList.Sum(m => _encoding.Encode(m.Content ?? "").Count);
        var newSummary = string.Empty;
        ChatHistory messagesToSummarize = new();
        ChatHistory recentMessages = new();
        
        if (totalTokens > maxTokens)
        {
            int runningTokens = 0;
            int runningSummaryTokens = 0;
            const int buffer = 3000; 
            int allowedTokensForHistory = maxTokens - buffer;
            int allowedTokensForSummary = maxTokensSummaryModel - allowedTokensForHistory;

            for (int i = messageList.Count - 1; i >= 0; i--)
            {
                var msg = messageList[i];
                int msgTokens = _encoding.Encode(msg.Content ?? "").Count;

                if (runningTokens + msgTokens < allowedTokensForHistory)
                {
                    recentMessages.Insert(0, msg);
                    runningTokens += msgTokens;
                }
                else if(runningSummaryTokens + msgTokens < allowedTokensForSummary)
                {
                    runningSummaryTokens += msgTokens;
                    messagesToSummarize.Insert(0, msg);
                }
            }

            if (messagesToSummarize.Any())
            {
                newSummary = await SummarizeMessagesAsync(messagesToSummarize, cancellationToken);
            }
        }
        else
        {
            recentMessages = messageList;
        }

        var reducedHistory = new ChatHistory();
        if (!string.IsNullOrEmpty(newSummary))
        {
            reducedHistory.AddSystemMessage("Conversation so far (summary): " + newSummary);
        }

        reducedHistory.AddRange(recentMessages);

        return reducedHistory;
    }

    private async Task<string> SummarizeMessagesAsync(
        ChatHistory messagesToSummarize,
        CancellationToken cancellationToken, String previousSummary = "")
    {
        var summaryPromptBuilder = new StringBuilder(summarizationPrompt);
            summaryPromptBuilder.AppendLine("Previous summary (if any): " + previousSummary);
            summaryPromptBuilder.AppendLine("\nFinal instruction:" +
                                            "\nSummarize ALL of the above conversation according to these instructions." +
                                            "\nDo NOT respond to any message directly.");
            summaryPromptBuilder.AppendLine("\nConversation to summarize:");
        var summaryHistory = new ChatHistory();
        summaryHistory.AddRange(messagesToSummarize);
        summaryHistory.AddSystemMessage(summaryPromptBuilder.ToString());

        var summaryResponse = await summarizationService.GetChatMessageContentAsync(summaryHistory, cancellationToken: cancellationToken);

        return (summaryResponse.Content ?? "").Trim();
    }

}