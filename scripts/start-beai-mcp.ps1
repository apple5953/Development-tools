param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
$McpDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\BEAI\MCP"))

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js 22 or later is required."
}

Push-Location $McpDir
try {
    if (-not (Test-Path "node_modules")) {
        npm.cmd install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed." }
    }
    if (-not $SkipBuild) {
        npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw "MCP build failed." }
    }
    npm.cmd start
} finally {
    Pop-Location
}
