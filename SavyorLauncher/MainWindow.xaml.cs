using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SavyorLauncher.Models;
using SavyorLauncher.Services;

namespace SavyorLauncher
{
    public partial class MainWindow : Window
    {
        private static readonly string AppBaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string VersionFilePath = Path.Combine(AppBaseDir, "version.json");
        private static readonly string StagingDir = Path.Combine(AppBaseDir, "staging");
        private static readonly string AppExePath = Path.Combine(AppBaseDir, "SavyorApp.exe");
        private static readonly string LauncherExePath = Path.Combine(AppBaseDir, "SavyorLauncher.exe");

        private readonly UpdateService _updateService;
        private readonly CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            _updateService = new UpdateService(VersionFilePath);
            _cts = new CancellationTokenSource();
            Loaded += async (s, e) => await RunLauncherSequenceAsync();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            Application.Current.Shutdown();
        }

        private string GetManifestUrl()
        {
            string configPath = Path.Combine(AppBaseDir, "launcher_config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("manifest_url", out var prop))
                    {
                        return prop.GetString() ?? "https://raw.githubusercontent.com/Realwaan/savyor/master/manifest.json";
                    }
                }
                catch
                {
                    // Fallback to default
                }
            }
            return "https://raw.githubusercontent.com/Realwaan/savyor/master/manifest.json";
        }

        private void SetStatus(string message, double progressVal)
        {
            Console.WriteLine($"[STATUS] {message} ({progressVal:F0}%)");
            Dispatcher.Invoke(() =>
            {
                StatusBox.Text = message;
                ProgressBox.Value = progressVal;
                ProgressPercentage.Text = $"{(int)progressVal}%";
            });
        }

        private async Task RunLauncherSequenceAsync()
        {
            try
            {
                // Parse command line args to see if we just updated
                string[] args = Environment.GetCommandLineArgs();
                var localVer = _updateService.GetLocalVersion();

                // If --updated arg is passed, we check if there is a version parameter
                if (args.Contains("--updated"))
                {
                    int idx = Array.IndexOf(args, "--updated");
                    if (idx + 1 < args.Length)
                    {
                        localVer.LauncherVersion = args[idx + 1];
                        _updateService.SaveLocalVersion(localVer);
                    }
                    SetStatus("Launcher updated successfully!", 100);
                    await Task.Delay(1000);
                }

                LauncherVersionLabel.Text = $"Launcher: v{localVer.LauncherVersion}";
                AppVersionLabel.Text = $"App: v{localVer.Version}";

                SetStatus("Checking update manifest...", 5);
                await Task.Delay(500);

                string manifestUrl = GetManifestUrl();
                var manifest = await _updateService.FetchRemoteManifestAsync(manifestUrl);

                if (manifest == null)
                {
                    SetStatus("Manifest server offline. Checking local files...", 30);
                    await Task.Delay(800);
                    VerifyAndLaunchExistingApp();
                    return;
                }

                // 1. Check launcher updates first
                if (UpdateService.IsUpdateAvailable(localVer.LauncherVersion, manifest.LauncherVersion))
                {
                    SetStatus($"Downloading Launcher v{manifest.LauncherVersion}...", 20);
                    string launcherZipPath = Path.Combine(StagingDir, "launcher.zip");
                    
                    var downloadProgress = new Progress<double>(val =>
                    {
                        SetStatus($"Downloading Launcher Update... {val:F0}%", 20 + (val * 0.4));
                    });

                    await _updateService.DownloadFileAsync(manifest.LauncherDownloadUrl, launcherZipPath, downloadProgress, _cts.Token);

                    SetStatus("Verifying launcher file integrity...", 70);
                    bool isLauncherValid = await _updateService.VerifyFileHashAsync(launcherZipPath, manifest.LauncherSha256);
                    if (!isLauncherValid)
                    {
                        MessageBox.Show("Launcher update corrupted. Skipping self-update.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        SetStatus("Installing Launcher Update...", 85);
                        string launcherExtractDir = Path.Combine(StagingDir, "launcher_new");
                        if (Directory.Exists(launcherExtractDir)) Directory.Delete(launcherExtractDir, true);

                        await Task.Run(() => _updateService.ExtractZipSafely(launcherZipPath, launcherExtractDir));

                        SetStatus("Restarting Savyor Launcher...", 95);
                        await Task.Delay(800);

                        localVer.LauncherVersion = manifest.LauncherVersion;
                        _updateService.SaveLocalVersion(localVer);

                        string currentExeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "SavyorLauncher.exe");
                        _updateService.GenerateLauncherSelfUpdateScript(launcherExtractDir, AppBaseDir, currentExeName);

                        // Spawn batch file to replace files and restart
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Path.Combine(AppBaseDir, "update_launcher.bat"),
                            WorkingDirectory = AppBaseDir,
                            UseShellExecute = true,
                            CreateNoWindow = true
                        });

                        Application.Current.Shutdown();
                        return;
                    }
                }

                // 2. Check if main app components are missing
                var missingFiles = new System.Collections.Generic.List<string>();
                if (manifest.RequiredFiles != null && manifest.RequiredFiles.Count > 0)
                {
                    foreach (var file in manifest.RequiredFiles)
                    {
                        string fullPath = Path.Combine(AppBaseDir, file);
                        if (!File.Exists(fullPath))
                        {
                            missingFiles.Add(file);
                        }
                    }
                }
                else
                {
                    // Fallback to checking SavyorApp.exe if none specified
                    if (!File.Exists(AppExePath)) missingFiles.Add("SavyorApp.exe");
                }

                bool appUpdateAvailable = UpdateService.IsUpdateAvailable(localVer.Version, manifest.Version);
                bool needsDownload = appUpdateAvailable || missingFiles.Count > 0;

                if (needsDownload)
                {
                    string statusMsg = appUpdateAvailable 
                        ? $"Updating Savyor App to v{manifest.Version}..." 
                        : $"Restoring {missingFiles.Count} missing component(s)...";

                    SetStatus(statusMsg, 30);
                    string appZipPath = Path.Combine(StagingDir, "app.zip");

                    var downloadProgress = new Progress<double>(val =>
                    {
                        SetStatus($"{statusMsg} {val:F0}%", 30 + (val * 0.5));
                    });

                    await _updateService.DownloadFileAsync(manifest.DownloadUrl, appZipPath, downloadProgress, _cts.Token);

                    SetStatus("Verifying file integrity...", 85);
                    bool isAppValid = await _updateService.VerifyFileHashAsync(appZipPath, manifest.Sha256);
                    if (!isAppValid)
                    {
                        MessageBox.Show("Downloaded application package failed checksum verification.", "Integrity Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        VerifyAndLaunchExistingApp();
                        return;
                    }

                    // Terminate running SavyorApp processes to prevent file lock errors during extraction
                    try
                    {
                        var processes = Process.GetProcessesByName("SavyorApp");
                        foreach (var proc in processes)
                        {
                            try
                            {
                                string? procPath = proc.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(procPath) && 
                                    string.Equals(Path.GetDirectoryName(procPath), AppBaseDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    proc.Kill();
                                    proc.WaitForExit(3000);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    SetStatus("Extracting application components...", 90);
                    await Task.Run(() => _updateService.ExtractZipSafely(appZipPath, AppBaseDir));

                    // Save new version configuration
                    localVer.Version = manifest.Version;
                    localVer.LauncherVersion = manifest.LauncherVersion;
                    _updateService.SaveLocalVersion(localVer);
                }

                // Clean up staging folder
                if (Directory.Exists(StagingDir))
                {
                    try
                    {
                        Directory.Delete(StagingDir, true);
                    }
                    catch { }
                }

                SetStatus("Launching Savyor App...", 100);
                await Task.Delay(800);
                LaunchMainApp();
            }
            catch (OperationCanceledException)
            {
                // App is shutting down
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Startup exception: {ex.Message}\n{ex.StackTrace}");
                SetStatus($"Startup error: {ex.Message}", 0);
                MessageBox.Show($"Launcher Error: {ex.Message}\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                VerifyAndLaunchExistingApp();
            }
        }

        private void VerifyAndLaunchExistingApp()
        {
            if (File.Exists(AppExePath))
            {
                SetStatus("Starting existing application...", 100);
                LaunchMainApp();
            }
            else
            {
                MessageBox.Show("The launcher was unable to connect to the update server and no local application executable could be found.\n\nPlease check your internet connection.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void LaunchMainApp()
        {
            if (File.Exists(AppExePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppExePath,
                    WorkingDirectory = AppBaseDir,
                    UseShellExecute = true
                });
            }
            Application.Current.Shutdown();
        }
    }
}