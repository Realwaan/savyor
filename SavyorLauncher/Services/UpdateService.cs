using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SavyorLauncher.Models;

namespace SavyorLauncher.Services
{
    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly string _localVersionPath;

        public UpdateService(string localVersionPath)
        {
            _localVersionPath = localVersionPath;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SavyorLauncher/1.0");
        }

        public LocalVersion GetLocalVersion()
        {
            try
            {
                if (!File.Exists(_localVersionPath))
                {
                    return new LocalVersion { Version = "0.0.0", LauncherVersion = "1.0.0" };
                }

                string json = File.ReadAllText(_localVersionPath);
                return JsonSerializer.Deserialize<LocalVersion>(json)
                       ?? new LocalVersion { Version = "0.0.0", LauncherVersion = "1.0.0" };
            }
            catch
            {
                return new LocalVersion { Version = "0.0.0", LauncherVersion = "1.0.0" };
            }
        }

        public void SaveLocalVersion(LocalVersion version)
        {
            try
            {
                version.LastUpdated = DateTime.UtcNow;
                string json = JsonSerializer.Serialize(version, new JsonSerializerOptions { WriteIndented = true });
                
                string dir = Path.GetDirectoryName(_localVersionPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(_localVersionPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save local version: {ex.Message}");
            }
        }

        public async Task<LauncherManifest?> FetchRemoteManifestAsync(string manifestUrl)
        {
            try
            {
                if (manifestUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(manifestUrl);
                    string localPath = uri.LocalPath;
                    if (!File.Exists(localPath)) return null;

                    string json = await File.ReadAllTextAsync(localPath);
                    return JsonSerializer.Deserialize<LauncherManifest>(json);
                }

                string responseJson = await _httpClient.GetStringAsync(manifestUrl);
                return JsonSerializer.Deserialize<LauncherManifest>(responseJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch remote manifest: {ex.Message}");
                return null;
            }
        }

        public static bool IsUpdateAvailable(string currentVersion, string remoteVersion)
        {
            if (Version.TryParse(currentVersion, out var local) &&
                Version.TryParse(remoteVersion, out var remote))
            {
                return remote > local;
            }
            return false;
        }

        public async Task DownloadFileAsync(string url, string destinationPath, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                string localFilePath = uri.LocalPath;

                if (!File.Exists(localFilePath))
                {
                    throw new FileNotFoundException("Mock update package not found at: " + localFilePath);
                }

                using var source = File.OpenRead(localFilePath);
                using var destination = File.Create(destinationPath);

                byte[] buffer = new byte[81920];
                long totalBytes = source.Length;
                long bytesRead = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, read, cancellationToken);
                    bytesRead += read;
                    if (totalBytes > 0)
                    {
                        progress.Report((double)bytesRead * 100 / totalBytes);
                    }
                }
                return;
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytesNullable = response.Content.Headers.ContentLength;
            long totalBytesCount = totalBytesNullable ?? -1L;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] downloadBuffer = new byte[81920];
            long downloadedBytes = 0;
            int bytesReadCount;

            while ((bytesReadCount = await contentStream.ReadAsync(downloadBuffer, 0, downloadBuffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(downloadBuffer, 0, bytesReadCount, cancellationToken);
                downloadedBytes += bytesReadCount;
                if (totalBytesCount > 0)
                {
                    progress.Report((double)downloadedBytes * 100 / totalBytesCount);
                }
            }
        }

        public async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash)) return true; // Skip if no hash provided

            try
            {
                if (!File.Exists(filePath)) return false;

                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(filePath);

                byte[] hashBytes = await sha256.ComputeHashAsync(stream);
                string computedHash = BitConverter.ToString(hashBytes)
                    .Replace("-", "")
                    .ToLowerInvariant();

                return string.Equals(
                    computedHash,
                    expectedHash.ToLowerInvariant(),
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public void ExtractZipSafely(string zipPath, string destinationDir)
        {
            string fullDestination = Path.GetFullPath(destinationDir);
            if (!fullDestination.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDestination += Path.DirectorySeparatorChar;
            }

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));

                if (!destinationPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"ZIP entry '{entry.FullName}' resolves outside target directory.");
                }

                string? entryDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }
        }

        public void GenerateLauncherSelfUpdateScript(string sourceDir, string targetDir, string currentExeName)
        {
            string batPath = Path.Combine(targetDir, "update_launcher.bat");
            string batContent = $@"@echo off
title Updating Savyor Launcher...
echo Waiting for launcher to exit...
:: Wait 2 seconds via ping
ping 127.0.0.1 -n 3 > NUL
echo Copying new launcher files from ""{sourceDir}"" to ""{targetDir}""...
xcopy /y /e /q ""{sourceDir}\*"" ""{targetDir}\""
if errorlevel 1 (
    echo Error copying files! Please run as administrator or close open processes.
    pause
    exit /b 1
)
echo Cleaning up staging files...
rmdir /s /q ""{sourceDir}""
echo Restarting launcher...
start """" ""{Path.Combine(targetDir, currentExeName)}"" --updated
echo Done!
del ""%~f0""
";
            File.WriteAllText(batPath, batContent);
        }
    }
}
