$creds = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($creds | Where-Object { $_ -match "^password=" }) -replace "^password=",""
if ($token) {
    echo $token | gh auth login --with-token
    gh release create v2.1.2 dist\DevelopmentTools_v2.1.2.zip --title "v2.1.2" --notes "Auto release v2.1.2"
} else {
    Write-Host "Failed to extract token"
}
