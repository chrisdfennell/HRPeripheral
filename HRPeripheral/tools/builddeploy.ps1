<# =======================
 build-install-run.ps1
------------------------
What it does:
- Interactively asks for device connection details.
- Cleans (light)
- Builds & packages APK
- (Optionally) pairs & connects to the watch over Wi-Fi ADB
- Uninstalls old app
- Installs new APK
- Launches app
- Streams logcat filtered to the app PID
======================= #>

# =======================
# CONFIG (Edit these if they don't change often)
# =======================
$Package = "com.fennell.hrperipheral"
$Configuration = "Debug"
$Framework = "net9.0-android"

# =======================
# INTERACTIVE SETUP
# =======================
$ErrorActionPreference = "Stop"

Write-Host "--- Device Configuration ---" -ForegroundColor Cyan
$DeviceIpInput = Read-Host "Enter the watch's IP Address (or press Enter for 192.168.86.28)"
if ([string]::IsNullOrWhiteSpace($DeviceIpInput)) {
    $DeviceIp = "192.168.86.28"
} else {
    $DeviceIp = $DeviceIpInput
}

$AdbPortInput = Read-Host "Enter the watch's ADB Port (or press Enter for 44139)"
if ([string]::IsNullOrWhiteSpace($AdbPortInput)) {
    $AdbPort = "44139"
} else {
    $AdbPort = $AdbPortInput
}

$WatchAdb = "$($DeviceIp):$($AdbPort)"
Write-Host "Using device address: $WatchAdb" -ForegroundColor Yellow

$needsPairing = Read-Host "Do you need to PAIR the device first? [y/n]"
if ($needsPairing -eq 'y') {
    $PairPort = Read-Host "Enter the Pairing Port"
    $PairCode = Read-Host "Enter the Pairing Code shown on the watch"
    $PairIpPort = "$($DeviceIp):$($PairPort)"
}

$needsConnecting = Read-Host "Do you need to CONNECT to the device? (Enter 'n' if already connected) [y/n]"

# =======================
# PREP
# =======================
Write-Host "==> Ensuring one MSBuild node and no fast-deploy leftovers..." -ForegroundColor Green
$env:MSBUILDDISABLENODEREUSE = "1"

# Optional: quick clean of obj/bin to avoid stale fast-deploy bits
if (Test-Path ".\bin") { Remove-Item -Recurse -Force ".\bin" }
if (Test-Path ".\obj") { Remove-Item -Recurse -Force ".\obj" }

# =======================
# BUILD + PACKAGE
# =======================
Write-Host "==> Restoring & building APK..." -ForegroundColor Green
dotnet build `
  -c $Configuration `
  -f $Framework `
  -t:PackageForAndroid `
  -m:1

Write-Host "==> Locating newest APK..." -ForegroundColor Green
$apk = Get-ChildItem -Path ".\bin\$Configuration\$Framework" -Filter *.apk |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

if (-not $apk) {
  throw "No APK found under .\bin\$Configuration\$Framework. Build may have failed."
}

Write-Host ("     APK: " + $apk.FullName)

# =======================
# ADB PAIR / CONNECT
# =======================
# Pair (only if values provided)
if ($PairIpPort -and $PairCode) {
  Write-Host "==> Pairing with $PairIpPort ..." -ForegroundColor Green
  adb pair $PairIpPort $PairCode
}

# Connect (skip if user said no)
if ($needsConnecting -ne 'n') {
    Write-Host "==> Connecting to $WatchAdb ..." -ForegroundColor Green
    adb connect $WatchAdb | Out-Null
}

# Quick verify
Write-Host "==> Verifying connection..." -ForegroundColor Green
$devices = adb devices | Out-String
Write-Host $devices
if ($devices -notlike "*$WatchAdb*") {
  throw "Watch not connected. Please check IP/Port and try again."
}

# =======================
# INSTALL
# =======================
Write-Host "==> Uninstalling old app (ignore failure if not installed)..." -ForegroundColor Green
# Use -ErrorAction SilentlyContinue to prevent script from stopping if uninstall fails (e.g., app not present)
adb -s $WatchAdb uninstall $Package -ErrorAction SilentlyContinue | Out-Null

Write-Host "==> Installing new APK..." -ForegroundColor Green
adb -s $WatchAdb install --no-incremental -r -t -g "$($apk.FullName)"

# =======================
# LAUNCH
# =======================
Write-Host "==> Launching app..." -ForegroundColor Green
adb -s $WatchAdb shell monkey -p $Package -c android.intent.category.LAUNCHER 1 | Out-Null

# =======================
# LOGCAT (filter to app PID)
# =======================
Write-Host "==> Fetching app PID..." -ForegroundColor Green
# Try a few times in case the process starts slowly
$AppPid = ""
for ($i=0; $i -lt 10; $i++) {
  # Robustly check for PID
  $pidOutput = adb -s $WatchAdb shell pidof $Package
  if ($pidOutput) {
    $AppPid = $pidOutput.Trim()
    if ($AppPid) { break }
  }
  Start-Sleep -Milliseconds 500
  Write-Host "     (retrying...)"
}
if (-not $AppPid) {
  Write-Warning "App PID not found. Streaming all logs tagged by package (may be noisy)."
  adb -s $WatchAdb logcat -v time | Select-String -Pattern $Package
  exit 0
}

Write-Host "==> Streaming logcat for PID $AppPid (Press Ctrl+C to stop)..." -ForegroundColor Green
adb -s $WatchAdb logcat --pid=$AppPid