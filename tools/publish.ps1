# Publishes WallStretch to GitHub and creates a release.
# Usage:
#   $env:GH_TOKEN = 'github_pat_...'
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
#
# Token: fine-grained PAT for the WallStretch repo with "Contents: Read and write".
# The token is never written to disk by this script.

param(
    [string]$Token = $env:GH_TOKEN,
    [string]$Owner = 'nikkawaf-rgb',
    [string]$Repo = 'WallStretch',
    [string]$Tag = 'v1.0.0',
    [string]$Title = 'WallStretch 1.0.0',
    [string]$NotesFile = (Join-Path $PSScriptRoot 'release-notes.md'),
    [string]$Asset = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\WallStretch-1.0.0.zip')
)

$ErrorActionPreference = 'Stop'
if (-not $Token) { throw 'Set $env:GH_TOKEN (fine-grained PAT with Contents: Read and write).' }

$root = Split-Path -Parent $PSScriptRoot
$headers = @{
    Authorization = "Bearer $Token"
    'User-Agent'  = 'WallStretch-release'
    Accept        = 'application/vnd.github+json'
}
$api = "https://api.github.com/repos/$Owner/$Repo"

$files = @('README.md', 'LICENSE', '.gitignore', 'build.ps1', 'app.ico')
$files += (Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { 'src/' + $_.Name })
$files += (Get-ChildItem (Join-Path $root 'screenshots') -Filter *.jpg | ForEach-Object { 'screenshots/' + $_.Name })

foreach ($rel in $files) {
    $local = Join-Path $root ($rel -replace '/', '\')
    $content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($local))

    $body = @{ message = "Add $rel"; content = $content; branch = 'main' } | ConvertTo-Json -Compress
    try {
        Invoke-RestMethod -Method Put -Uri "$api/contents/$rel" -Headers $headers -Body $body -ContentType 'application/json' | Out-Null
        Write-Host "added   $rel"
    }
    catch {
        $existing = Invoke-RestMethod -Uri "$api/contents/$rel?ref=main" -Headers $headers
        $body = @{ message = "Update $rel"; content = $content; branch = 'main'; sha = $existing.sha } | ConvertTo-Json -Compress
        Invoke-RestMethod -Method Put -Uri "$api/contents/$rel" -Headers $headers -Body $body -ContentType 'application/json' | Out-Null
        Write-Host "updated $rel"
    }
}

$notes = Get-Content -LiteralPath $NotesFile -Raw
$releaseBody = @{
    tag_name         = $Tag
    name             = $Title
    body             = $notes
    draft            = $false
    prerelease       = $false
    target_commitish = 'main'
} | ConvertTo-Json -Compress

try {
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -Body $releaseBody -ContentType 'application/json'
    Write-Host "release created: $($release.html_url)"
}
catch {
    $release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
    Write-Host "release exists: $($release.html_url)"
}

if ($release.assets | Where-Object { $_.name -eq (Split-Path $Asset -Leaf) }) {
    Write-Host "asset already uploaded"
}
else {
    $name = Split-Path $Asset -Leaf
    $upload = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($name))"
    Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -InFile $Asset -ContentType 'application/zip' | Out-Null
    Write-Host "asset uploaded: $name"
}

Write-Host "done: https://github.com/$Owner/$Repo/releases/tag/$Tag"
