using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace SavyorApp.Services
{
    public static class DocxReader
    {
        public static string ReadText(string filePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var documentEntry = archive.GetEntry("word/document.xml");
                if (documentEntry == null)
                {
                    return "Error: This file does not appear to be a valid Word document (word/document.xml is missing).";
                }

                using var stream = documentEntry.Open();
                var doc = XDocument.Load(stream);

                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var sb = new StringBuilder();

                foreach (var paragraph in doc.Descendants(w + "p"))
                {
                    var pText = new StringBuilder();
                    foreach (var text in paragraph.Descendants(w + "t"))
                    {
                        pText.Append(text.Value);
                    }
                    
                    if (pText.Length > 0)
                    {
                        sb.AppendLine(pText.ToString());
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error parsing Word document: {ex.Message}";
            }
        }
    }
}
