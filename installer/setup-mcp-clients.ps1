[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'DevelopmentTools'),
    [switch]$DetectOnly
)

$ErrorActionPreference = 'Stop'
$mcpEntry = Join-Path $InstallRoot 'App\MCP\dist\index.js'
$logDir = Join-Path $InstallRoot 'Logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logPath = Join-Path $logDir 'mcp-client-setup.log'

function Write-Status([string]$message) {
    $line = '[{0}] {1}' -f (Get-Date -Format s), $message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Find-Command([string]$name) {
    $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    foreach ($candidate in @(
        (Join-Path $env:APPDATA "npm\$name.cmd"),
        (Join-Path $env:LOCALAPPDATA "Programs\nodejs\$name.exe"),
        (Join-Path $env:ProgramFiles "nodejs\$name.exe")
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return $null
}

function Set-McpJson([string]$path, [string]$nodePath) {
    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $json = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw -Encoding UTF8 } else { '{}' }
    if ([string]::IsNullOrWhiteSpace($json)) { $json = '{}' }
    $config = ConvertFrom-Json -InputObject $json
    if ($null -eq $config) { $config = [pscustomobject]@{} }
    if (-not $config.PSObject.Properties['mcpServers']) {
        $config | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
    }
    $entry = [pscustomobject]@{ command = $nodePath; args = @($mcpEntry, '--stdio') }
    if ($config.mcpServers.PSObject.Properties['beai-reality-bridge']) {
        $config.mcpServers.'beai-reality-bridge' = $entry
    } else {
        $config.mcpServers | Add-Member -NotePropertyName 'beai-reality-bridge' -NotePropertyValue $entry
    }
    if (Test-Path -LiteralPath $path) {
        Copy-Item -LiteralPath $path -Destination ($path + '.bak-' + (Get-Date -Format yyyyMMddHHmmss))
    }
    $tempPath = "$path.tmp-$PID"
    try {
        [IO.File]::WriteAllText($tempPath, ($config | ConvertTo-Json -Depth 30), (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $tempPath -Destination $path -Force
    } finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
    }
    Write-Status "Registered MCP in $path"
}

if (-not (Test-Path -LiteralPath $mcpEntry -PathType Leaf)) {
    throw "MCP adapter missing: $mcpEntry"
}

$node = Find-Command 'node'
if (-not $node) {
    Write-Status 'Node.js 22+ is required; MCP client registration was skipped.'
    exit 1
}
$major = [int]((& $node --version).TrimStart('v').Split('.')[0])
if ($major -lt 22) {
    Write-Status "Node.js $major is too old; version 22+ is required."
    exit 1
}
Write-Status "MCP runtime: $node"

if ($DetectOnly) {
    foreach ($name in @('codex', 'claude')) {
        $found = Find-Command $name
        Write-Status "$name : $(if ($found) { $found } else { 'not found' })"
    }
    exit 0
}

foreach ($client in @(
    @{ Name = 'Codex'; Exe = 'codex' },
    @{ Name = 'Claude Code'; Exe = 'claude' }
)) {
    $command = Find-Command $client.Exe
    if (-not $command) {
        Write-Status "$($client.Name) unavailable; skipped registration."
        continue
    }
    try {
        if ($client.Exe -eq 'codex') {
            & $command mcp remove beai-reality-bridge 2>$null | Out-Null
            & $command mcp add beai-reality-bridge -- $node $mcpEntry --stdio
        } else {
            & $command mcp remove beai-reality-bridge -s user 2>$null | Out-Null
            & $command mcp add beai-reality-bridge --scope user -- $node $mcpEntry --stdio
        }
        if ($LASTEXITCODE -ne 0) { throw "client exited $LASTEXITCODE" }
        Write-Status "$($client.Name) MCP registered."
    } catch {
        Write-Status "$($client.Name) MCP registration failed: $($_.Exception.Message)"
    }
}

$claudeDesktop = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
if (Test-Path -LiteralPath (Split-Path -Parent $claudeDesktop)) {
    try { Set-McpJson $claudeDesktop $node }
    catch { Write-Status "Claude Desktop configuration failed: $($_.Exception.Message)" }
}

$antigravityPaths = @(
    (Join-Path $env:USERPROFILE '.gemini\antigravity\mcp_config.json'),
    (Join-Path $env:USERPROFILE '.gemini\config\mcp_config.json'),
    (Join-Path $env:USERPROFILE '.gemini\antigravity-ide\mcp_config.json')
)
foreach ($path in @($antigravityPaths | Where-Object { Test-Path -LiteralPath (Split-Path -Parent $_) })) {
    try { Set-McpJson $path $node }
    catch { Write-Status "Antigravity configuration failed: $($_.Exception.Message)" }
}

Write-Status 'MCP client setup complete; each client may require sign-in and restart.'
