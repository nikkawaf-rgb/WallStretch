# Build WallStretch (C# 5, .NET Framework 4.x, no external dependencies)

$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found' }

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    ('/out:' + (Join-Path $root 'WallStretch.exe'))
    '/r:System.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
) + $sources

$icon = Join-Path $root 'app.ico'
if (Test-Path $icon) { $cscArgs = @('/win32icon:' + $icon) + $cscArgs }

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with code $LASTEXITCODE" }

Write-Host ("OK: " + (Join-Path $root 'WallStretch.exe')) -ForegroundColor Green
