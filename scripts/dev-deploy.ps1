# Dev deploy: build DevelopmentTools (Debug, no obfuscation) and copy it to the folder Revit 2024 loads.
# Close Revit first. Run via scripts\dev-deploy.cmd (double-click).
param(
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
    throw "Revit is still running (DLLs are locked). Close Revit and run again."
}

# 1. Find where Revit 2024 loads DevelopmentTools.Addin.dll from (.addin manifests)
$addinDirs = @(
    (Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"),
    (Join-Path $env:ProgramData "Autodesk\Revit\Addins\2024")
)
$targets = @()
foreach ($dir in $addinDirs) {
    if (-not (Test-Path $dir)) { continue }
    Get-ChildItem $dir -Filter "*.addin" | ForEach-Object {
        try {
            [xml]$xml = Get-Content $_.FullName -Raw -Encoding UTF8
            foreach ($a in $xml.RevitAddIns.AddIn) {
                if ($a.Assembly -and $a.Assembly -like "*DevelopmentTools.Addin.dll") {
                    $asm = $a.Assembly
                    if (-not [System.IO.Path]::IsPathRooted($asm)) { $asm = Join-Path $dir $asm }
                    Write-Output "Revit manifest: $($_.FullName)"
                    Write-Output "  -> $asm"
                    $targets += (Split-Path -Parent $asm)
                }
            }
        } catch { }
    }
}
$targets = @($targets | Select-Object -Unique)
if ($targets.Count -eq 0) {
    throw "No .addin in the Revit 2024 Addins folder points to DevelopmentTools.Addin.dll. Install the add-in once with the installer first."
}

# 2. Build the whole solution
Write-Output "Building DevelopmentTools.sln ($Configuration)..."
dotnet build (Join-Path $root "DevelopmentTools.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED (exit $LASTEXITCODE). See the errors above." }

$binDir = Join-Path $root "DevelopmentTools.Addin\bin\$Configuration\net48"
if (-not (Test-Path (Join-Path $binDir "DevelopmentTools.Addin.dll"))) { throw "Build output not found: $binDir" }

# 3. Copy DLLs (including the built-in modules) and resource folders
foreach ($t in $targets) {
    if ([System.IO.Path]::GetFullPath($t).TrimEnd('\') -ieq [System.IO.Path]::GetFullPath($binDir).TrimEnd('\')) {
        Write-Output "Revit loads directly from the build folder; nothing to copy: $t"
        continue
    }
    Write-Output "Copying to $t"
    Copy-Item (Join-Path $binDir "*.dll") $t -Force
    foreach ($sub in @("Resources", "Rules", "Runtime")) {
        $src = Join-Path $binDir $sub
        if (Test-Path $src) { Copy-Item $src $t -Recurse -Force }
    }
}

Write-Output ""
Write-Output "DEPLOY OK. Start Revit 2024 and look for the 'Development tools' tab."
Write-Output "Note: if Revit's auto-update installs the GitHub release on startup, it will overwrite this test build; run this script again."
