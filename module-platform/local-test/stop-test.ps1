$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifest = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\DevelopmentTools.addin'
$backup = Join-Path $repoRoot 'dist\local-test\DevelopmentTools.addin.original'

if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before restoring the add-in manifest.' }
if (-not (Test-Path -LiteralPath $backup)) { throw "Original manifest backup not found: $backup" }
Copy-Item -LiteralPath $backup -Destination $manifest -Force
Remove-Item -LiteralPath $backup -Force
Write-Host "Restored original Revit add-in manifest: $manifest"
