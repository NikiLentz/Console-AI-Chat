using System.ComponentModel;
using System.Text;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace ConsoleAIChat.Plugins;

public class CodeInterpreterPlugin(ILogger<CodeInterpreterPlugin> logger)
{
    [KernelFunction, Description("Executes Javascript code. The result of the last expression is returned. Ensure it's a string or a number, not an object. For multiple outputs, use console.log(). You can use this to perform calculations, manipulate data, or generate content dynamically. Don't access the internet or any external apis.")]
    public async Task<string> ExecuteCodeAsync([Description("The JS Code to execute")]string code, CancellationToken ct = default)
    {
        logger.LogInformation("AI tried code: {Code}", code);
        var output = new StringBuilder();
        
        var engine = new Engine(options => {
            options.TimeoutInterval(TimeSpan.FromSeconds(2));
            options.MaxStatements(100000); // Instruction limit
            options.LimitMemory(4_000_000); // 4MB memory limit
            options.LimitRecursion(100);
        });
        
        // Add console.log
        engine.SetValue("console", new {
            log = new Action<object>(obj => output.AppendLine(obj?.ToString()))
        });
        
        try
        {
            var result = await Task.Run(() => engine.Evaluate(code), ct);
            if (!result.IsUndefined())
                output.AppendLine($"=> {result}");
            var finalOutput = output.ToString();
            logger.LogInformation("AI code output: {Output}", finalOutput);
            return finalOutput;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("AI code execution timed out.");
            return "Error: Execution timed out.";
        }
        catch (ExecutionCanceledException)
        {
            logger.LogWarning("AI code exceeded execution limits.");
            return "Error: Execution exceeded limits.";
        }
        catch (JavaScriptException ex)
        {
            return $"JavaScript Error: {ex.Message}";
        }
    }
}