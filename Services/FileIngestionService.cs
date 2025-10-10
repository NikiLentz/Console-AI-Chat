using System.Text;
using ConsoleAIChat.Database;
using ConsoleAIChat.Services.Interfaces;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ConsoleAIChat.Services;

public class FileIngestionService(IDbContextFactory<AppDbContext> contextFactory, IConfiguration configuration, ILogger<FileIngestionService> logger, IVectorService vectorService):IFileIngestionService
{
    private readonly string folderPath = configuration["FileIngestion:FolderPath"] ?? 
        throw new ArgumentNullException("FileIngestion:FolderPath configuration is missing");
    
    

    public async Task IngestFilesAsync(CancellationToken cancellationToken = default)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var maxChunkSize = int.Parse(configuration["FileIngestion:MaxChunkSize"] ?? "1000");
        var overlapSize = int.Parse(configuration["FileIngestion:OverlapSize"] ?? "200");
        var filePaths = Directory.GetFiles(folderPath);
        foreach (var path in filePaths)
        {
            
            var chunks = new List<string>();
            StringBuilder sb = new StringBuilder();
            var file = new FileInfo(path);
            if (context.VectorDatabaseFiles.Any(f => f.FileName == file.Name))
            {
                //logger.LogInformation("File {FilePath} already ingested, skipping.", path);
                continue;
            }
            if(file.Extension == ".ppt" || file.Extension == ".pptx")
            {
                var numberOfSlides = CountSlides(path);
                logger.LogInformation("File {FilePath} has {NumberOfSlides} slides.", path, numberOfSlides);
                for (var i = 0;  i < numberOfSlides; i++)
                {
                    var text = GetSlideIdAndText(path, i);
                    sb.AppendLine(text);
                    sb.AppendLine("\n");
                    logger.LogDebug("Extracted text from slide {SlideIndex}: {Text}", i, text);
                }
                chunks = CreateChunksWithOverlap(sb.ToString(), maxChunkSize, overlapSize);
            } 
            else if (file.Extension == ".pdf")
            {
                sb.Clear();
                var fileBytes = await File.ReadAllBytesAsync(path, cancellationToken);
                using var pdf = PdfDocument.Open(fileBytes);
                foreach (var page in pdf.GetPages())
                {
                    IEnumerable<Word> words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                    foreach (Word word in words)
                    {
                        sb.Append(word.Text + " ");
                    } 
                    sb.AppendLine("\n");
                }
                
                chunks = CreateChunksWithOverlap(sb.ToString(), maxChunkSize, overlapSize);
            }
            else
            {
                logger.LogWarning("Unsupported file type: {FileExtension}", file.Extension);
            }
            await vectorService.IngestTextAsync(chunks.ToArray(), file.Name, cancellationToken);
            
        }
    }
    
    private static int CountSlides(string presentationFile)
    {
        using (PresentationDocument presentationDocument = PresentationDocument.Open(presentationFile, false))
        {
            return CountSlidesFromPresentation(presentationDocument);
        }
    }
    
    
    private static int CountSlidesFromPresentation(PresentationDocument presentationDocument)
    {
        if (presentationDocument is null)
        {
            throw new ArgumentNullException("presentationDocument");
        }
        int slidesCount = 0;
        PresentationPart? presentationPart = presentationDocument.PresentationPart;
        if (presentationPart is not null)
        {
            slidesCount = presentationPart.SlideParts.Count();
        }
        return slidesCount;
    }

    private static string GetSlideIdAndText(string docName, int index)
{
    using var ppt = PresentationDocument.Open(docName, false);
    var part = ppt.PresentationPart;
    var slideIds = part?.Presentation?.SlideIdList?.ChildElements ?? default;
    if (part is null || slideIds.Count == 0)
    {
        return "";
    }

    string? relId = ((SlideId)slideIds[index]).RelationshipId;

    if (relId is null)
    {
        return "";
    }
    
    var slide = (SlidePart)part.GetPartById(relId);
    var result = new StringBuilder();

    // Process all shapes in the slide
    var shapes = slide.Slide.Descendants<Shape>();
    foreach (var shape in shapes)
    {
        if (shape.TextBody != null)
        {
            result.AppendLine(shape.TextBody.InnerText);
        }
    }

    // Process all tables - optimized for vector embeddings
    var tables = slide.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Table>();
    foreach (var table in tables)
    {
        var rows = table.Descendants<DocumentFormat.OpenXml.Drawing.TableRow>().ToList();
        
        // Get headers from first row if available
        if (rows.Count > 0)
        {
            var headerCells = rows[0].Descendants<DocumentFormat.OpenXml.Drawing.TableCell>()
                .Select(cell => cell.InnerText.Trim())
                .ToArray();
            
            // Process data rows
            for (int i = 1; i < rows.Count; i++)
            {
                var dataCells = rows[i].Descendants<DocumentFormat.OpenXml.Drawing.TableCell>()
                    .Select(cell => cell.InnerText.Trim())
                    .ToArray();
                
                // Create semantic row: "Header1: Value1. Header2: Value2."
                for (int j = 0; j < Math.Min(headerCells.Length, dataCells.Length); j++)
                {
                    if (!string.IsNullOrWhiteSpace(dataCells[j]))
                    {
                        result.AppendLine($"{headerCells[j]}: {dataCells[j]}");
                    }
                }
                result.AppendLine(); // Blank line between rows
            }
        }
    }

    return result.ToString();
}
    
    private List<String> CreateChunksWithOverlap(string text, int maxChunkSize, int overlapSize)
    {
        var chunks = new List<string>();
        var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    
        if (words.Length == 0) return chunks;
    
        int position = 0;
        while (position < words.Length)
        {
            var chunkWords = words.Skip(position).Take(maxChunkSize).ToArray();
            chunks.Add(string.Join(" ", chunkWords));
            position += maxChunkSize - overlapSize;
            if (overlapSize >= maxChunkSize)
            {
                position = words.Length;
            }
        }
    
        return chunks;
    }
    
}