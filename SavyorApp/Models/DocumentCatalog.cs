using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SavyorApp.Models
{
    public class DocumentInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("extension")]
        public string Extension { get; set; } = "";

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; } = 0;

        [JsonPropertyName("date_added")]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class CatalogData
    {
        [JsonPropertyName("documents")]
        public List<DocumentInfo> Documents { get; set; } = new List<DocumentInfo>();
    }
}
