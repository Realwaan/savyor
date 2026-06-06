using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SavyorApp.Services
{
    public static class PptxReader
    {
        public static string ReadText(string filePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var sb = new StringBuilder();

                var slideEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && 
                                 e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e =>
                    {
                        string numStr = new string(e.Name.Where(char.IsDigit).ToArray());
                        return int.TryParse(numStr, out int num) ? num : 9999;
                    })
                    .ToList();

                if (slideEntries.Count == 0)
                {
                    return "Error: No slide layouts detected in PowerPoint presentation.";
                }

                XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

                for (int i = 0; i < slideEntries.Count; i++)
                {
                    sb.AppendLine($"--- Slide {i + 1} ---");
                    using var stream = slideEntries[i].Open();
                    var doc = XDocument.Load(stream);

                    foreach (var paragraph in doc.Descendants(a + "p"))
                    {
                        var pText = new StringBuilder();
                        foreach (var text in paragraph.Descendants(a + "t"))
                        {
                            pText.Append(text.Value);
                        }

                        if (pText.Length > 0)
                        {
                            sb.AppendLine(pText.ToString());
                        }
                    }
                    sb.AppendLine();
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error parsing PowerPoint presentation: {ex.Message}";
            }
        }
    }
}
