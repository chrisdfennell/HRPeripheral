<# =======================
 builddeploy.ps1
-------------------------
- Asks Debug/Release
- Cleans (light), builds + publishes APK
- (Optional) ADB pair + connect over Wi-Fi
- Uninstalls old app, installs APK, launches
- Streams logcat for the app PID
======================= #>

param(
    [string]$Framework = "net9.0-android",
    [string]$Package = "com.fennell.hrperipheral"
)

$ErrorActionPreference = "Stop"

# Always run from repo root (one level up from /tools)
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function Select-Configuration {
    Write-Host "Select configuration:" -ForegroundColor Cyan
    Write-Host "[1] Debug"
    Write-Host "[2] Release"
    $choice = Read-Host "Enter 1 or 2"
    switch ($choice) {
        "1" { return "Debug" }
        "2" { return "Release" }
        default { throw "Invalid choice. Please run again and enter 1 or 2." }
    }
}

$Configuration = Select-Configuration

Write-Host "--- Device Configuration ---" -ForegroundColor Cyan
$DeviceIpInput = Read-Host "Enter the watch's IP Address (default 192.168.86.28)"
$DeviceIp = if ([string]::IsNullOrWhiteSpace($DeviceIpInput)) { "192.168.86.28" } else { $DeviceIpInput }

$AdbPortInput = Read-Host "Enter the watch's ADB Port (default 44139)"
$AdbPort = if ([string]::IsNullOrWhiteSpace($AdbPortInput)) { "44139" } else { $AdbPortInput }

$WatchAdb = "$($DeviceIp):$($AdbPort)"
Write-Host "Using device address: $WatchAdb" -ForegroundColor Yellow

$needsPairing = Read-Host "Do you need to PAIR the device first? [y/n]"
if ($needsPairing -eq 'y') {
    $PairPort = Read-Host "Enter the Pairing Port"
    $PairCode = Read-Host "Enter the Pairing Code shown on the watch"
    $PairIpPort = "$($DeviceIp):$($PairPort)"
}

$needsConnecting = Read-Host "Do you need to CONNECT to the device? (Enter 'n' if already connected) [y/n]"

# Prep
Write-Host "==> Ensuring a single MSBuild node and no stale artifacts..." -ForegroundColor Green
$env:MSBUILDDISABLENODEREUSE = "1"

# Optional light clean (keeps NuGet caches)
if (Test-Path ".\bin") { Remove-Item -Recurse -Force ".\bin" -ErrorAction SilentlyContinue }
if (Test-Path ".\obj") { Remove-Item -Recurse -Force ".\obj" -ErrorAction SilentlyContinue }

# Build + package (publish) an APK
Write-Host "==> Restoring & publishing APK ($Configuration)..." -ForegroundColor Green
dotnet publish .\HRPeripheral.csproj `
  -c $Configuration `
  -f $Framework `
  /p:AndroidPackageFormat=apk `
  -m:1

# Find newest APK (handles publish/ subfolder too)
Write-Host "==> Locating newest APK..." -ForegroundColor Green
$apk = Get-ChildItem -Path ".\bin\$Configuration\$Framework" -Recurse -Filter *.apk |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

if (-not $apk) {
  throw "No APK found under .\bin\$Configuration\$Framework. Build may have failed."
}
Write-Host ("     APK: " + $apk.FullName)

# Pair (optional)
if ($PairIpPort -and $PairCode) {
  Write-Host "==> Pairing with $PairIpPort ..." -ForegroundColor Green
  adb pair $PairIpPort $PairCode
}

# Connect (optional)
if ($needsConnecting -ne 'n') {
    Write-Host "==> Connecting to $WatchAdb ..." -ForegroundColor Green
    adb connect $WatchAdb | Out-Null
}

# Verify
Write-Host "==> Verifying connection..." -ForegroundColor Green
$devices = adb devices | Out-String
Write-Host $devices
if ($devices -notlike "*$WatchAdb*") {
  throw "Watch not connected. Check IP/Port and try again."
}

# Install
Write-Host "==> Uninstalling old app (ignore failure if not installed)..." -ForegroundColor Green
adb -s $WatchAdb uninstall $Package -ErrorAction SilentlyContinue | Out-Null

Write-Host "==> Installing new APK..." -ForegroundColor Green
adb -s $WatchAdb install --no-incremental -r -t -g "$($apk.FullName)"

# Launch
Write-Host "==> Launching app..." -ForegroundColor Green
adb -s $WatchAdb shell monkey -p $Package -c android.intent.category.LAUNCHER 1 | Out-Null

# Logcat (PID-filtered)
Write-Host "==> Fetching app PID..." -ForegroundColor Green
$AppPid = ""
for ($i=0; $i -lt 10; $i++) {
  $pidOutput = adb -s $WatchAdb shell pidof $Package
  if ($pidOutput) {
    $AppPid = $pidOutput.Trim()
    if ($AppPid) { break }
  }
  Start-Sleep -Milliseconds 500
  Write-Host "     (retrying...)"
}
if (-not $AppPid) {
  Write-Warning "App PID not found. Streaming logs filtered by package name."
  adb -s $WatchAdb logcat -v time | Select-String -Pattern $Package
  exit 0
}

Write-Host "==> Streaming logcat for PID $AppPid (Ctrl+C to stop)..." -ForegroundColor Green
adb -s $WatchAdb logcat --pid=$AppPid