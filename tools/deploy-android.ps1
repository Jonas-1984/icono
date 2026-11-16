<#
.SYNOPSIS
  Builds the Android app of icono (src\Icono.Mobile.Android) and installs and starts it on the tablet connected by USB.
.NOTES
  Needs: .NET 10 SDK with the android workload, JDK 17 (JAVA_HOME), Android SDK (ANDROID_HOME, default %LOCALAPPDATA%\Android\Sdk).
  The tablet needs USB debugging on; Xiaomi devices also "Install via USB", and they ask on the screen to confirm every install.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\deploy-android.ps1
  powershell -ExecutionPolicy Bypass -File tools\deploy-android.ps1 -Configuration Release -NoInstall
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Icono.Mobile.Android\Icono.Mobile.Android.csproj'
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$appId = 'de.bitbins.icono'

dotnet build $project -c $Configuration "-p:AndroidSdkDirectory=$sdk"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$apk = Get-ChildItem (Join-Path $root "src\Icono.Mobile.Android\bin\$Configuration") -Recurse -Filter "$appId-Signed.apk" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw 'No APK found.' }
Write-Host ("APK: {0} ({1:0.0} MB)" -f $apk.FullName, ($apk.Length / 1MB))

if ($NoInstall) { return }

Write-Host 'Installing - confirm "Install" on the tablet if it asks ...'
& $adb install -r --no-incremental $apk.FullName
if ($LASTEXITCODE -ne 0) { throw 'Install failed (was it confirmed on the tablet?).' }

& $adb shell am force-stop $appId
& $adb shell monkey -p $appId -c android.intent.category.LAUNCHER 1 | Out-Null
Write-Host 'Started on the tablet.'
