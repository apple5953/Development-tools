param(
    [string]$BridgeUrl = "http://127.0.0.1:8765/api/v1/execute",
    [string]$Category = "Doors",
    [string]$Level,
    [long[]]$ElementIds = @()
)

$ErrorActionPreference = "Stop"

function Invoke-BeaiTool {
    param([string]$Tool, [hashtable]$Args = @{})
    $body = @{
        requestId = [guid]::NewGuid().ToString()
        version = "1.0"
        tool = $Tool
        timestamp = [DateTime]::UtcNow.ToString("o")
        args = $Args
        context = @{ source = "test-client"; client = "beai-v01-powershell" }
    } | ConvertTo-Json -Depth 12

    $result = Invoke-RestMethod -Uri $BridgeUrl -Method Post -ContentType "application/json" -Body $body -TimeoutSec 35
    if (-not $result.success) {
        $errorText = ($result.errors | ForEach-Object { "$($_.code): $($_.message)" }) -join "; "
        throw "$Tool failed: $errorText"
    }
    Write-Host "[PASS] $Tool ($($result.durationMs) ms)" -ForegroundColor Green
    return $result.data
}

Write-Host "BEAI Reality Bridge V0.1 real-model smoke test" -ForegroundColor Cyan
$health = Invoke-BeaiTool "system.health"
$document = Invoke-BeaiTool "document.get_active"
$levels = Invoke-BeaiTool "level.list"
$queryArgs = @{ category = $Category; limit = 100; cursor = 1 }
if ($Level) { $queryArgs.level = $Level }
$elements = Invoke-BeaiTool "element.query" $queryArgs
if ($ElementIds.Count -gt 0) {
    $null = Invoke-BeaiTool "element.get_parameters" @{ elementIds = $ElementIds }
}

[pscustomobject]@{
    Bridge = $health.bridge
    Document = $document.documentTitle
    Levels = $levels.total
    QueryCategory = $Category
    QueryLevel = $Level
    Elements = $elements.items.Count
} | Format-List
