param(
    [string]$RevitVersion = "2024",
    [switch]$AllUsers,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RTS] $Message"
}

function Escape-Xml {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $packageRoot "RoomTileSystem"
$pluginName = "RoomTileSystem"
$manifestName = "RoomTileSystem.addin"

if ($AllUsers) {
    $addinRoot = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitVersion"
} else {
    $addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

$targetDir = Join-Path $addinRoot $pluginName
$manifestPath = Join-Path $addinRoot $manifestName

if ($Uninstall) {
    Write-Step "Removing manifest: $manifestPath"
    if (Test-Path $manifestPath) {
        (Get-Item -LiteralPath $manifestPath).IsReadOnly = $false
        Remove-Item -LiteralPath $manifestPath -Force
    }

    Write-Step "Removing add-in files: $targetDir"
    if (Test-Path $targetDir) {
        Get-ChildItem -LiteralPath $targetDir -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }

    Write-Step "Done."
    exit 0
}

Write-Step "Package folder: $packageRoot"
Write-Step "Install target: $addinRoot"

if (-not (Test-Path $sourceDir)) {
    throw "Missing folder: $sourceDir. Please extract the zip file completely before running install.bat."
}

$sourceDll = Join-Path $sourceDir "RoomTileSystem.Addin.dll"
if (-not (Test-Path $sourceDll)) {
    throw "Missing add-in DLL: $sourceDll"
}

$sourceConfig = Join-Path $sourceDir "platform_config.json"
if (-not (Test-Path $sourceConfig)) {
    throw "Missing config file: $sourceConfig"
}

$revitExe = Join-Path ${env:ProgramFiles} "Autodesk\Revit $RevitVersion\Revit.exe"
if (-not (Test-Path $revitExe)) {
    Write-Warning "Revit $RevitVersion was not found at the default path. The add-in will still be installed for Revit $RevitVersion."
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

if (Test-Path $targetDir) {
    Get-ChildItem -LiteralPath $targetDir -Recurse -File | ForEach-Object { $_.IsReadOnly = $false }
}
if (Test-Path $manifestPath) {
    (Get-Item -LiteralPath $manifestPath).IsReadOnly = $false
}

if (Test-Path $manifestPath) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupPath = "$manifestPath.bak.$stamp"
    Write-Step "Backing up existing manifest to: $backupPath"
    Copy-Item -LiteralPath $manifestPath -Destination $backupPath -Force
}

Write-Step "Copying add-in files..."
Copy-Item -Path (Join-Path $sourceDir "*") -Destination $targetDir -Recurse -Force

try {
    Get-ChildItem -LiteralPath $targetDir -Recurse -File | Unblock-File
} catch {
    Write-Warning "Could not unblock one or more copied files. If Revit blocks the add-in, right-click the zip before extraction and choose Properties > Unblock."
}

$assemblyPath = Join-Path $targetDir "RoomTileSystem.Addin.dll"
$assemblyPathXml = Escape-Xml $assemblyPath

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Development tools</Name>
    <Assembly>$assemblyPathXml</Assembly>
    <FullClassName>RoomTileSystem.App</FullClassName>
    <ClientId>c2d5d85c-4d33-4f9e-a89e-21ef1ea3b361</ClientId>
    <VendorId>MAYOUCHR</VendorId>
    <VendorDescription>MAYOUCHR Development tools Addin Suite</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Write-Step "Writing Revit manifest..."
New-Item -ItemType Directory -Path $addinRoot -Force | Out-Null
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

Get-ChildItem -LiteralPath $targetDir -Recurse -File | ForEach-Object { $_.IsReadOnly = $true }
(Get-Item -LiteralPath $manifestPath).IsReadOnly = $true

Write-Step "Installed files:"
Write-Host "  $targetDir"
Write-Host "  $manifestPath"
Write-Step "Done. Restart Revit $RevitVersion, then choose Always Load if Revit asks for confirmation."
