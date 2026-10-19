param([switch]$LaunchRevit)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$candidate = Join-Path $repoRoot 'DevelopmentTools.Addin\bin\Debug\net48\DevelopmentTools.Addin.dll'
$manifest = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\DevelopmentTools.addin'
$backupDir = Join-Path $repoRoot 'dist\local-test'
$backup = Join-Path $backupDir 'DevelopmentTools.addin.original'

if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before switching the add-in manifest.' }
if (-not (Test-Path -LiteralPath $manifest)) { throw "Existing manifest not found: $manifest" }
if (Test-Path -LiteralPath $backup) { throw "A test backup already exists. Restore it first: $backup" }

dotnet build (Join-Path $repoRoot 'DevelopmentTools.Addin\DevelopmentTools.Addin.csproj') -c Debug -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Debug build failed; the installed manifest was not changed.' }
if (-not (Test-Path -LiteralPath $candidate)) { throw "Candidate DLL missing: $candidate" }
if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $candidate) 'ExampleAgent.dll'))) { throw 'Pilot module DLL missing.' }

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
Copy-Item -LiteralPath $manifest -Destination $backup
try {
    [xml]$addin = Get-Content -LiteralPath $manifest -Raw
    $entry = @($addin.RevitAddIns.AddIn | Where-Object { $_.FullClassName -eq 'DevelopmentTools.App' })
    if ($entry.Count -ne 1) { throw 'Expected exactly one DevelopmentTools.App registration.' }
    $entry[0].Assembly = $candidate
    $addin.Save($manifest)
} catch {
    Copy-Item -LiteralPath $backup -Destination $manifest -Force
    Remove-Item -LiteralPath $backup -Force
    throw
}

Write-Host "Local test registration now points to: $candidate"
Write-Host "Original manifest backed up at: $backup"
Write-Host 'After testing, close Revit and run module-platform\local-test\stop-test.ps1.'
if ($LaunchRevit) {
    Start-Process -FilePath 'C:\Program Files\Autodesk\Revit 2024\Revit.exe' -WindowStyle Normal
}
