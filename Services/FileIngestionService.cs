using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ConsoleAIChat.Services;

public class FileIngestionService(IConfiguration configuration, ILogger<FileIngestionService> logger)
{
    private readonly string folderPath = configuration["FileIngestion:FolderPath"] ?? 
        throw new ArgumentNullException("FileIngestion:FolderPath configuration is missing");

    public async Task IngestFilesAsync(CancellationToken cancellationToken = default)
    {
        var filePaths = Directory.GetFiles(folderPath);
        foreach (var path in filePaths)
        {
            var file = new FileInfo(path);
            if(file.Extension == ".pptx" || file.Extension == ".pptx")
            {
                var numberOfSlides = CountSlides(path);
                Console.WriteLine($"Number of slides = {numberOfSlides}");

                for (var i = 0;  i < numberOfSlides; i++)
                {
                    var text = GetSlideIdAndText(path, i);
                    Console.WriteLine($"Side #{i + 1} contains: {text}");
                }
                
            }
            
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

        var paragraphText = new StringBuilder();

        IEnumerable<Text> texts = slide.Slide.Descendants<Text>();
        foreach (var text in texts)
        {
            paragraphText.Append(text.Text);
        }
        return paragraphText.ToString();
    }

    
}