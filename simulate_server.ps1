# Savyor Build, Packaging & Self-Update Simulator
# Run this script to test launcher, auto-update, and repair features.

$ErrorActionPreference = "Stop"
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Savyor Application Self-Update Simulation Setup" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

# 1. Setup paths
$RootFolder = "F:\savyor"
$BuildFolder = "$RootFolder\Build"
$MockServerFolder = "$RootFolder\MockServer"
$ClientAppFolder = "$RootFolder\ClientApp"

# Ensure output folders are fresh
if (Test-Path $BuildFolder) { Remove-Item -Recurse -Force $BuildFolder }
if (Test-Path $MockServerFolder) { Remove-Item -Recurse -Force $MockServerFolder }
if (Test-Path $ClientAppFolder) { Remove-Item -Recurse -Force $ClientAppFolder }

New-Item -ItemType Directory -Path $MockServerFolder | Out-Null
New-Item -ItemType Directory -Path $ClientAppFolder | Out-Null

# Helper to compute SHA256 hash as string
function Get-FileSha256($filePath) {
    $hash = (Get-FileHash -Algorithm SHA256 $filePath).Hash.ToLowerInvariant()
    return $hash
}

# 2. Build SavyorApp (Main Application)
Write-Host "`n[1/5] Compiling SavyorApp..." -ForegroundColor Yellow
dotnet publish "$RootFolder\SavyorApp\SavyorApp.csproj" -c Release -r win-x64 --self-contained false -o "$BuildFolder\App"

# 3. Build SavyorLauncher (Launcher)
Write-Host "[2/5] Compiling SavyorLauncher..." -ForegroundColor Yellow
dotnet publish "$RootFolder\SavyorLauncher\SavyorLauncher.csproj" -c Release -r win-x64 --self-contained false -o "$BuildFolder\Launcher"

# 4. Package SavyorApp Updates
Write-Host "[3/5] Packaging SavyorApp Zip Releases..." -ForegroundColor Yellow

# App v1.0.0 Package
$AppStaging100 = "$BuildFolder\App_1.0.0"
New-Item -ItemType Directory -Path $AppStaging100 | Out-Null
Copy-Item -Path "$BuildFolder\App\*" -Destination $AppStaging100 -Recurse -Force
# Create empty placeholder document index
New-Item -ItemType Directory -Path "$AppStaging100\documents" | Out-Null
'{"documents":[]}' | Out-File -FilePath "$AppStaging100\documents\documents.json" -Encoding utf8 -Force
Compress-Archive -Path "$AppStaging100\*" -DestinationPath "$MockServerFolder\SavyorApp_1.0.0.zip" -Force
$AppHash100 = Get-FileSha256 "$MockServerFolder\SavyorApp_1.0.0.zip"

# App v1.1.0 Package (Add a release notes file and some new documents)
$AppStaging110 = "$BuildFolder\App_1.1.0"
New-Item -ItemType Directory -Path $AppStaging110 | Out-Null
Copy-Item -Path "$BuildFolder\App\*" -Destination $AppStaging110 -Recurse -Force
"New Features in 1.1.0:`n- Added openXML reader for docx/pptx`n- Premium UI refinements`n- Checksum verifiers" | Out-File -FilePath "$AppStaging110\release_notes.txt" -Encoding utf8 -Force
Compress-Archive -Path "$AppStaging110\*" -DestinationPath "$MockServerFolder\SavyorApp_1.1.0.zip" -Force
$AppHash110 = Get-FileSha256 "$MockServerFolder\SavyorApp_1.1.0.zip"

# 5. Package Launcher Updates (v1.1.0)
Write-Host "[4/5] Packaging SavyorLauncher Zip Releases..." -ForegroundColor Yellow
$LauncherStaging110 = "$BuildFolder\Launcher_1.1.0"
New-Item -ItemType Directory -Path $LauncherStaging110 | Out-Null
Copy-Item -Path "$BuildFolder\Launcher\*" -Destination $LauncherStaging110 -Recurse -Force
# Add a dummy file inside launcher staging to prove self-update extracted successfully
"Launcher Core Updated: v1.1.0" | Out-File -FilePath "$LauncherStaging110\launcher_changelog.txt" -Encoding utf8 -Force
Compress-Archive -Path "$LauncherStaging110\*" -DestinationPath "$MockServerFolder\SavyorLauncher_1.1.0.zip" -Force
$LauncherHash110 = Get-FileSha256 "$MockServerFolder\SavyorLauncher_1.1.0.zip"

# 6. Generate Manifest configurations
Write-Host "[5/5] Generating Manifest manifests..." -ForegroundColor Yellow

# Manifest for v1.1.0 updates
$ManifestJson = @"
{
  "version": "1.1.0",
  "launcher_version": "1.1.0",
  "download_url": "file:///$($MockServerFolder.Replace('\', '/'))/SavyorApp_1.1.0.zip",
  "launcher_download_url": "file:///$($MockServerFolder.Replace('\', '/'))/SavyorLauncher_1.1.0.zip",
  "sha256": "$AppHash110",
  "launcher_sha256": "$LauncherHash110",
  "required_files": [
    "SavyorApp.exe",
    "SavyorApp.dll",
    "SavyorApp.runtimeconfig.json"
  ]
}
"@
$ManifestJson | Out-File -FilePath "$MockServerFolder\manifest.json" -Encoding utf8 -Force

# Manifest for v1.0.0 updates (for testing transition increments)
$ManifestJson100 = @"
{
  "version": "1.0.0",
  "launcher_version": "1.0.0",
  "download_url": "file:///$($MockServerFolder.Replace('\', '/'))/SavyorApp_1.0.0.zip",
  "launcher_download_url": "file:///$($MockServerFolder.Replace('\', '/'))/SavyorLauncher_1.1.0.zip",
  "sha256": "$AppHash100",
  "launcher_sha256": "$LauncherHash110",
  "required_files": [
    "SavyorApp.exe",
    "SavyorApp.dll",
    "SavyorApp.runtimeconfig.json"
  ]
}
"@
$ManifestJson100 | Out-File -FilePath "$MockServerFolder\manifest_1.0.0.json" -Encoding utf8 -Force

# 7. Initialize ClientApp environment at v1.0.0 / launcher v1.0.0
# Copy launcher files
Copy-Item -Path "$BuildFolder\Launcher\*" -Destination $ClientAppFolder -Recurse -Force
# Create local version.json pointing to launcher v1.0.0, App v0.0.0 (meaning missing)
$VersionJson = @"
{
  "version": "0.0.0",
  "launcher_version": "1.0.0",
  "last_updated": "2026-06-06T00:00:00Z"
}
"@
$VersionJson | Out-File -FilePath "$ClientAppFolder\version.json" -Encoding utf8 -Force

# Configure manifest pointer
$ConfigJson = @"
{
  "manifest_url": "file:///$($MockServerFolder.Replace('\', '/'))/manifest.json"
}
"@
$ConfigJson | Out-File -FilePath "$ClientAppFolder\launcher_config.json" -Encoding utf8 -Force

Write-Host "`nSetup complete! Simulation workspace ready." -ForegroundColor Green
Write-Host "Target location: $ClientAppFolder" -ForegroundColor Cyan
Write-Host "Test scenarios you can run:" -ForegroundColor Green
Write-Host "  1. Run Launcher (checks files, finds launcher needs v1.1.0 update, runs self-update, restarts, then downloads SavyorApp v1.1.0)." -ForegroundColor Gray
Write-Host "     Command: start $ClientAppFolder\SavyorLauncher.exe" -ForegroundColor Gray
Write-Host "  2. Test Integrity recovery: delete SavyorApp.exe from ClientApp and run Launcher. It will re-download files." -ForegroundColor Gray
Write-Host "     Command: Remove-Item $ClientAppFolder\SavyorApp.exe; start $ClientAppFolder\SavyorLauncher.exe" -ForegroundColor Gray
