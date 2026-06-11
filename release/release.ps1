param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$Channel = "stable",
    [string]$Owner = "apple5953",
    [string]$Repo = "Development-tools"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$parentRoot = Split-Path -Parent $root

# Ensure GitHub CLI path is in process PATH
$ghPath = "C:\Program Files\GitHub CLI"
if (Test-Path (Join-Path $ghPath "gh.exe")) {
    if ($env:Path -notlike "*$ghPath*") {
        $env:Path = "$env:Path;$ghPath"
    }
}

# Ensure Inno Setup path is in process PATH
$innoPath = "C:\Users\User\AppData\Local\Programs\Inno Setup 6"
if (Test-Path (Join-Path $innoPath "ISCC.exe")) {
    if ($env:Path -notlike "*$innoPath*") {
        $env:Path = "$env:Path;$innoPath"
    }
}

# Automatically retrieve GitHub token from Git Credential Manager if not already set
if (-not $env:GH_TOKEN -and -not $env:GITHUB_TOKEN) {
    try {
        $inputStr = "protocol=https`nhost=github.com`n`n"
        $credOutput = $inputStr | git credential fill 2>$null
        foreach ($line in $credOutput) {
            if ($line -like "password=*") {
                $token = $line.Substring("password=".Length).Trim()
                if ($token) {
                    $env:GH_TOKEN = $token
                    Write-Host "[RTS] Automatically loaded GitHub token from Git Credential Manager."
                }
            }
        }
    } catch {
        # Silently ignore and let gh fail later if no token is found
    }
}

# 1. Check Git workspace status (Disabled by default for local testing)
# $gitStatus = git status --porcelain
# if ($gitStatus) {
#     Write-Warning "Git status is not clean! Please commit your changes first."
#     exit 1
# }

# 2. Build entire Solution
Write-Host "[RTS] Cleaning Solution in Release mode..."
dotnet clean (Join-Path $parentRoot "DevelopmentTools.sln") -c Release

Write-Host "[RTS] Building Solution in Release mode (No Incremental)..."
dotnet build (Join-Path $parentRoot "DevelopmentTools.sln") -c Release --no-incremental

# 3. Ensure obfuscated DLL is generated
$releaseDir = Join-Path $parentRoot "DevelopmentTools.Addin\bin\Release\net48"
$obfDll = Join-Path $releaseDir "Obfuscated\DevelopmentTools.Addin.dll"
if (-not (Test-Path $obfDll)) {
    throw "Obfuscated DLL not found at: $obfDll"
}

# 4. Staging files
$distDir = Join-Path $parentRoot "dist"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

$zipTempDir = Join-Path $parentRoot "dist\temp_zip"
if (Test-Path $zipTempDir) { Remove-Item -LiteralPath $zipTempDir -Recurse -Force }
New-Item -ItemType Directory -Path $zipTempDir | Out-Null

# Copy obfuscated DLL and shared parameters
Copy-Item -LiteralPath $obfDll -Destination $zipTempDir
Copy-Item -LiteralPath (Join-Path $parentRoot "DevelopmentTools.Addin\TileJointSharedParam.txt") -Destination $zipTempDir
Copy-Item -LiteralPath (Join-Path $parentRoot "DevelopmentTools.Addin\platform_config.json") -Destination $zipTempDir
Copy-Item -LiteralPath (Join-Path $parentRoot "DevelopmentTools.Addin\appsettings.json") -Destination $zipTempDir -ErrorAction SilentlyContinue

# Copy all dependent DLLs from the Addin build (excluding the un-obfuscated main DLL)
Copy-Item -Path (Join-Path $releaseDir "*.dll") -Destination $zipTempDir -Exclude "DevelopmentTools.Addin.dll"

# Copy Updater and all dependency DLLs into ZIP so it acts as both AutoUpdate package and Portable Green package
$updaterBinDir = Join-Path $parentRoot "DevelopmentTools.Updater\bin\Release\net48"
Copy-Item -Path (Join-Path $updaterBinDir "DevelopmentTools.Updater.exe") -Destination $zipTempDir
Copy-Item -Path (Join-Path $updaterBinDir "DevelopmentTools.Updater.exe.config") -Destination $zipTempDir
Copy-Item -Path (Join-Path $updaterBinDir "*.dll") -Destination $zipTempDir

# Copy Resources folder to ZIP
Copy-Item -Path (Join-Path $parentRoot "DevelopmentTools.Addin\Resources") -Destination $zipTempDir -Recurse

# Copy install.bat to ZIP
Copy-Item -LiteralPath (Join-Path $parentRoot "installer\install.bat") -Destination $zipTempDir

# Generate version.json
$versionJson = @{
    "app_id" = "development_tools"
    "product_name" = "Room Tile System v3"
    "current_version" = $Version
    "channel" = $Channel
    "main_dll" = "DevelopmentTools.Addin.dll"
    "install_folder" = "%LOCALAPPDATA%\DevelopmentTools\App"
    "updater_path" = "%LOCALAPPDATA%\DevelopmentTools\Updater\DevelopmentTools.Updater.exe"
    "manifest_url" = "https://raw.githubusercontent.com/$Owner/$Repo/main/update_manifest.json"
    "updated_at" = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
}
$versionJson | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $zipTempDir "version.json") -Encoding utf8

# 同步將產生的真實 version.json 複製到 bin/Release/net48，確保 Inno Setup 封裝時寫入正確的版本號
Copy-Item -LiteralPath (Join-Path $zipTempDir "version.json") -Destination (Join-Path $releaseDir "version.json") -Force

# Compress to ZIP
$zipName = "DevelopmentTools_v$Version.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $zipTempDir "*") -DestinationPath $zipPath

# Calculate SHA256
Write-Host "[RTS] Calculating ZIP SHA256 hash..."
$fileHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# 5. Update update_manifest.json
Write-Host "[RTS] Generating Remote Manifest..."
$manifest = @{
    "app_id" = "development_tools"
    "product_name" = "Room Tile System v3"
    "latest_version" = $Version
    "channel" = $Channel
    "release_url" = "https://github.com/$Owner/$Repo/releases/download/v$Version/$zipName"
    "sha256" = $fileHash
    "force_update" = $false
    "minimum_supported_version" = "1.0.0"
    "release_note" = "Remote update release v$Version."
    "published_at" = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
}
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $parentRoot "update_manifest.json") -Encoding utf8

# Clean up temp files
Remove-Item -LiteralPath $zipTempDir -Recurse -Force

# 6. Compile Inno Setup Installer
Write-Host "[RTS] Generating Inno Installer..."
$issPath = Join-Path $parentRoot "installer\inno\DevelopmentTools_Setup.iss"
if (Get-Command ISCC.exe -ErrorAction SilentlyContinue) {
    & ISCC.exe $issPath
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) is not in PATH. Skipping installer compilation. Please build manually using DevelopmentTools_Setup.iss."
}

# 7. Push release changes to Git
Write-Host "[RTS] Committing and pushing tag to GitHub..."
git add (Join-Path $parentRoot "update_manifest.json")
git commit -m "release: v$Version remote update manifest"
git push

git tag "v$Version"
git push origin "v$Version"

# 8. Create GitHub Release using gh CLI
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "[RTS] Creating GitHub Release using gh CLI..."
    $exePath = Join-Path $distDir "DevelopmentTools_Setup.exe"
    gh release create "v$Version" $zipPath $exePath --title "v$Version" --notes "Auto release v$Version"
} else {
    Write-Warning "gh CLI is not installed. Please upload the ZIP file manually to: https://github.com/$Owner/$Repo/releases/tag/v$Version"
}

Write-Host "[RTS] Release process successfully completed!"
