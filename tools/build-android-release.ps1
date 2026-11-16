<#
.SYNOPSIS
  Builds the signed APK of icono for tablets that is handed to testers: Release (without the catalogues of unclear origin), signed with the
  release key, copied to installer\Output\icono-android-<version>.apk with its SHA-256 in installer\Output\SHA256-android.txt.
.NOTES
  The release key is NOT in the repository (it is public): %USERPROFILE%\.icono-signing\icono-release.keystore and its password in
  icono-release.pass next to it. Keep both safe (a backup outside this PC): an APK signed with another key cannot update an installed icono.
  Before handing out a new APK raise ApplicationVersion (and ApplicationDisplayVersion) in src\Icono.Mobile.Android\Icono.Mobile.Android.csproj.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-android-release.ps1
  powershell -ExecutionPolicy Bypass -File tools\build-android-release.ps1 -Install
#>
param([switch]$Install)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Icono.Mobile.Android\Icono.Mobile.Android.csproj'
$signing = Join-Path $env:USERPROFILE '.icono-signing'
$keystore = Join-Path $signing 'icono-release.keystore'
$passFile = Join-Path $signing 'icono-release.pass'
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$appId = 'de.bitbins.icono'

if (-not (Test-Path $keystore) -or -not (Test-Path $passFile)) {
    throw "The release key is missing ($keystore). Restore it from the backup - a new key cannot update the installed apps."
}

# tests of the shared code first
dotnet test (Join-Path $root 'Icono.sln') -c Release -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root 'installer\Output'
New-Item -ItemType Directory -Force $out | Out-Null

# never reuse an APK of an earlier run (a failed signing once left an unsigned "-Signed.apk" behind that a later build took as done)
Remove-Item (Join-Path $root 'src\Icono.Mobile.Android\bin\Release') -Recurse -Force -ErrorAction SilentlyContinue

# the password file holds the password twice: apksigner reads line 1 for the key store and line 2 for the key when both name the same file
dotnet publish $project -c Release -f net10.0-android "-p:AndroidSdkDirectory=$sdk" `
    -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$keystore" -p:AndroidSigningKeyAlias=icono `
    "-p:AndroidSigningStorePass=file:$passFile" "-p:AndroidSigningKeyPass=file:$passFile" -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$apk = Get-ChildItem (Join-Path $root 'src\Icono.Mobile.Android\bin\Release') -Recurse -Filter "$appId-Signed.apk" |
    Where-Object { $_.FullName -match '\\publish\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw 'No signed APK found.' }

# the signature must be there and be ours, else the APK cannot be installed (or not updated) on other tablets
$apksigner = Get-ChildItem (Join-Path $sdk 'build-tools') -Recurse -Filter apksigner.jar | Sort-Object FullName -Descending | Select-Object -First 1
$java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\java.exe' } else { 'java' }
$check = & $java -jar $apksigner.FullName verify --print-certs $apk.FullName 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $check -notmatch 'CN=icono') { throw "The APK is not signed with the release key:`n$check" }

$target = Join-Path $out "icono-android-$version.apk"
Copy-Item $apk.FullName $target -Force
$hash = (Get-FileHash $target -Algorithm SHA256).Hash
"$hash  $(Split-Path $target -Leaf)" | Set-Content (Join-Path $out 'SHA256-android.txt') -Encoding ascii
Write-Host ("APK: {0} ({1:0.0} MB)" -f $target, ((Get-Item $target).Length / 1MB))
Write-Host "SHA-256: $hash"

if ($Install) {
    $adb = Join-Path $sdk 'platform-tools\adb.exe'
    Write-Host 'Installing - confirm "Install" on the tablet if it asks ...'
    & $adb install -r --no-incremental $target
    if ($LASTEXITCODE -ne 0) { throw 'Install failed (a development build signed with another key must be uninstalled first).' }
}
