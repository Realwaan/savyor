using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SavyorLauncher.Models
{
    public class LauncherManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("launcher_version")]
        public string LauncherVersion { get; set; } = "1.0.0";

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("launcher_download_url")]
        public string LauncherDownloadUrl { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("launcher_sha256")]
        public string LauncherSha256 { get; set; } = "";

        [JsonPropertyName("required_files")]
        public List<string> RequiredFiles { get; set; } = new List<string>();
    }
}
