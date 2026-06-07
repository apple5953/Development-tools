param(
    [string]$Configuration = "Release",
    [string]$RevitVersion = "2024",
    [string]$PackageName = "RTS_OneClick_Installer"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $root "RoomTileSystem.Addin\RoomTileSystem.Addin.csproj"
$outputDir = Join-Path $root "RoomTileSystem.Addin\bin\$Configuration\net48"
$templateDir = Join-Path $root "InstallerTemplate"
$packageDir = Join-Path $root $PackageName
$pluginDir = Join-Path $packageDir "RoomTileSystem"
$zipPath = Join-Path $root "$PackageName.zip"

if (-not (Test-Path $templateDir)) {
    throw "Missing installer template folder: $templateDir"
}

Write-Host "[RTS] Building $Configuration..."
dotnet build $projectPath -c $Configuration

if (Test-Path $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null

Copy-Item -Path (Join-Path $outputDir "*.dll") -Destination $pluginDir -Force
Copy-Item -Path (Join-Path $outputDir "platform_config.json") -Destination $pluginDir -Force

$obfuscatedDll = Join-Path $outputDir "Obfuscated\RoomTileSystem.Addin.dll"
if (-not (Test-Path $obfuscatedDll)) {
    throw "Missing obfuscated DLL: $obfuscatedDll"
}
Copy-Item -LiteralPath $obfuscatedDll -Destination (Join-Path $pluginDir "RoomTileSystem.Addin.dll") -Force

$sharedParam = Join-Path $root "RoomTileSystem.Addin\bin\Debug\net48\TileJointSharedParam.txt"
if (Test-Path $sharedParam) {
    Copy-Item -LiteralPath $sharedParam -Destination $pluginDir -Force
}

Copy-Item -LiteralPath (Join-Path $templateDir "install.bat") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $templateDir "uninstall.bat") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $templateDir "install.ps1") -Destination $packageDir -Force
Copy-Item -Path (Join-Path $templateDir "*.md") -Destination $packageDir -Force

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -Force

Write-Host "[RTS] Package created:"
Write-Host "  $packageDir"
Write-Host "  $zipPath"
