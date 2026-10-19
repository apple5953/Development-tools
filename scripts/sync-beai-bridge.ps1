param(
    [string]$BeaiRoot = "D:\安全讀取 Revit 模型資訊的通用橋接層\安全讀取 Revit 模型資訊的通用橋接層",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bridgeDir = Join-Path $root "DevelopmentTools.Addin\Bridge"
$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if (-not (Test-Path $BeaiRoot)) {
    throw "找不到 BEAI 原始碼：$BeaiRoot"
}

& $msbuild (Join-Path $BeaiRoot "src\BEAI.QueryHost\BEAI.QueryHost.csproj") "/p:Configuration=$Configuration" "/p:Platform=x64" "/v:m"
if ($LASTEXITCODE -ne 0) { throw "BEAI.QueryHost 建置失敗。" }

& $msbuild (Join-Path $BeaiRoot "src\BEAI.QueryWorker\BEAI.QueryWorker.csproj") "/p:Configuration=$Configuration" "/p:Platform=x64" "/v:m"
if ($LASTEXITCODE -ne 0) { throw "BEAI.QueryWorker 建置失敗。" }

New-Item -ItemType Directory -Force -Path $bridgeDir, (Join-Path $bridgeDir "Worker"), (Join-Path $bridgeDir "Rules") | Out-Null
Copy-Item -LiteralPath (Join-Path $BeaiRoot "src\BEAI.QueryHost\bin\x64\$Configuration\BEAI.QueryHost.dll") -Destination (Join-Path $bridgeDir "BEAI.QueryHost.dll") -Force
Copy-Item -LiteralPath (Join-Path $BeaiRoot "src\BEAI.QueryHost\bin\x64\$Configuration\BEAI.QueryHost.pdb") -Destination (Join-Path $bridgeDir "BEAI.QueryHost.pdb") -Force
Copy-Item -LiteralPath (Join-Path $BeaiRoot "bin\$Configuration\Worker\BEAI.QueryWorker.exe") -Destination (Join-Path $bridgeDir "Worker\BEAI.QueryWorker.exe") -Force
Copy-Item -LiteralPath "C:\Program Files\Autodesk\Revit 2024\Newtonsoft.Json.dll" -Destination (Join-Path $bridgeDir "Worker\Newtonsoft.Json.dll") -Force
Copy-Item -Path (Join-Path $BeaiRoot "Runtime\Rules\*.json") -Destination (Join-Path $bridgeDir "Rules") -Force

Write-Host "BEAI 橋接模組已同步至 Room Tile。" -ForegroundColor Green
