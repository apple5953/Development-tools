# Rename the office plug-in source archive:
#   "ai (2)"      -> office-plugins-source
#   "ai (2).zip"  -> office-plugins-source.zip
# Close Revit and any Explorer window showing that folder first. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File .\scripts\tidy-office-sources.ps1 [-RemoveOfficeToolkit]
param(
    [switch]$RemoveOfficeToolkit
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$oldDir = Join-Path $root "ai (2)"
$newDir = Join-Path $root "office-plugins-source"
$oldZip = Join-Path $root "ai (2).zip"
$newZip = Join-Path $root "office-plugins-source.zip"

if (Test-Path -LiteralPath $oldDir) {
    if (Test-Path -LiteralPath $newDir) { throw "$newDir already exists. Check it before continuing." }
    Rename-Item -LiteralPath $oldDir -NewName "office-plugins-source"
    Write-Output "Folder renamed: $newDir"
} elseif (Test-Path -LiteralPath $newDir) {
    Write-Output "Folder already renamed: $newDir"
} else {
    Write-Warning "Not found: $oldDir"
}

if (Test-Path -LiteralPath $oldZip) {
    if (Test-Path -LiteralPath $newZip) { throw "$newZip already exists. Check it before continuing." }
    Rename-Item -LiteralPath $oldZip -NewName "office-plugins-source.zip"
    Write-Output "Zip renamed: $newZip"
}

if ($RemoveOfficeToolkit) {
    $toolkit = Join-Path $newDir "OfficeToolkit"
    if (Test-Path -LiteralPath $toolkit) {
        Remove-Item -LiteralPath $toolkit -Recurse -Force
        Write-Output "Removed retired OfficeToolkit: $toolkit"
    }
}
Write-Output "Done."
