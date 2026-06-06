using System;
using System.Text.Json.Serialization;

namespace SavyorLauncher.Models
{
    public class LocalVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "0.0.0";

        [JsonPropertyName("launcher_version")]
        public string LauncherVersion { get; set; } = "1.0.0";

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
    }
}
