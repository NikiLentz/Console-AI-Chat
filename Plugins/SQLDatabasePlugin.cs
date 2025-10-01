using System.ComponentModel;
using ConsoleAIChat.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace ConsoleAIChat.Plugins;

public class SQLDatabasePlugin(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<SQLDatabasePlugin> logger)
{
    [KernelFunction, Description("Executes a read-only SQL query and returns the result as a string. Only SELECT statements are allowed. The AI can use this to query the database schema (e.g., by querying information_schema.tables and information_schema.columns) to understand the table structure before forming a data query.")]
    public async Task<string> QueryAsync([Description("The SQL SELECT statement to execute.")] string query, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AI tried query: {Query}", query);
        if (!query.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return "Error: Only SELECT statements are allowed.";
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = query;
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!reader.HasRows)
            {
                return "Query executed successfully, but returned no rows.";
            }

            var resultBuilder = new System.Text.StringBuilder();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                resultBuilder.Append(reader.GetName(i)).Append(i < reader.FieldCount - 1 ? "\t" : "");
            }

            resultBuilder.AppendLine();

            while (await reader.ReadAsync(cancellationToken))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    resultBuilder.Append(reader[i]).Append(i < reader.FieldCount - 1 ? "\t" : "");
                }

                resultBuilder.AppendLine();
            }

            return resultBuilder.ToString();


        }
        catch (Exception e)
        {
            return $"Error executing query: {e.Message}";
        }

    }
}