<#
.SYNOPSIS
  Builds the installer of icono: runs the tests, publishes the app (self-contained, Windows x64) and compiles installer\icono.iss.

.DESCRIPTION
  Result: installer\Output\ICONO-Setup-<version>.exe and installer\Output\SHA256.txt (the checksum the testers can compare).
  Needs: .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root 'installer'
$publish = Join-Path $installer 'publish'
$output = Join-Path $installer 'Output'

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'Inno Setup 6 (ISCC.exe) was not found. Install it with: winget install JRSoftware.InnoSetup'
}

$iscc = Find-InnoCompiler

if (-not $SkipTests) {
    Write-Host '== Tests (Release) ==' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'Icono.sln') -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw 'The tests failed: no installer was built.' }
}

Write-Host '== Publish (self-contained, win-x64) ==' -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Icono.App\Icono.App.csproj') -c Release -r win-x64 --self-contained true -o $publish -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host '== Installer (Inno Setup) ==' -ForegroundColor Cyan
if (-not (Test-Path $output)) { New-Item -ItemType Directory -Path $output | Out-Null }
& $iscc (Join-Path $installer 'icono.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Get-ChildItem $output -Filter 'ICONO-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash
$line = "$hash  $($setup.Name)"
Set-Content -Path (Join-Path $output 'SHA256.txt') -Value $line -Encoding ascii

Write-Host ''
Write-Host "Installer: $($setup.FullName)" -ForegroundColor Green
Write-Host ('Size:      {0:n1} MB' -f ($setup.Length / 1MB))
Write-Host "SHA-256:   $hash"
