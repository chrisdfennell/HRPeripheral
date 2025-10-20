<# =======================
 run.ps1  — HRPeripheral tool
-------------------------
Menu launcher for common tasks:
  1) Build APK
  2) Deploy latest APK (no build)
  3) Build + Deploy
  4) Clean (bin/obj)
  5) Pair/Connect over Wi-Fi ADB
  6) Change configuration
  7) Quit

Asks for Debug or Release Candidate (RC) at start.
Works when launched from /tools (auto cd to project root).
======================= #>

param(
  [string]$Framework = "net9.0-android",
  [string]$Package   = "com.fennell.hrperipheral"
)

$ErrorActionPreference = "Stop"

# Always run from repo root (one level up from /tools)
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function Select-Configuration {
  Write-Host "Select configuration:" -ForegroundColor Cyan
  Write-Host "[1] Debug"
  Write-Host "[2] Release Candidate"
  $choice = Read-Host "Enter 1 or 2"
  switch ($choice) {
    "1" { return @{ Configuration = "Debug";   VersionSuffix = $null } }
    "2" { return @{ Configuration = "Release"; VersionSuffix = "rc"   } }
    default { throw "Invalid choice. Please run again and enter 1 or 2." }
  }
}

function Prompt-Device {
  Write-Host "`n--- Device Configuration ---" -ForegroundColor Cyan
  $DeviceIp  = Read-Host "Watch IP (default 192.168.86.28)"
  if ([string]::IsNullOrWhiteSpace($DeviceIp)) { $DeviceIp = "192.168.86.28" }

  $AdbPort   = Read-Host "Watch ADB Port (default 44139)"
  if ([string]::IsNullOrWhiteSpace($AdbPort)) { $AdbPort = "44139" }

  $WatchAdb  = "$DeviceIp`:$AdbPort"

  $needsPair = Read-Host "Pair device first? [y/n]"
  $pairInfo  = $null
  if ($needsPair -eq 'y') {
    $PairPort = Read-Host "Pairing Port"
    $PairCode = Read-Host "Pairing Code (shown on watch)"
    $pairInfo = @{ PairIpPort = "$DeviceIp`:$PairPort"; PairCode = $PairCode }
  }

  $needsConn = Read-Host "Connect over Wi-Fi ADB now? [y/n] (enter 'n' if already connected)"

  return @{
    WatchAdb      = $WatchAdb
    PairInfo      = $pairInfo
    NeedsConnect  = ($needsConn -ne 'n')
  }
}

function Pair-Connect([string]$WatchAdb, $PairInfo) {
  if ($PairInfo -ne $null -and $PairInfo.PairIpPort -and $PairInfo.PairCode) {
    Write-Host "==> Pairing $($PairInfo.PairIpPort) ..." -ForegroundColor Green
    adb pair $PairInfo.PairIpPort $PairInfo.PairCode
  }

  Write-Host "==> Connecting to $WatchAdb ..." -ForegroundColor Green
  adb connect $WatchAdb | Out-Null

  Write-Host "==> Verifying connection..." -ForegroundColor Green
  $devices = adb devices | Out-String
  Write-Host $devices
  if ($devices -notlike "*$WatchAdb*") {
    throw "Watch not connected. Check IP/Port and try again."
  }
}

function Clean-Artifacts {
  Write-Host "==> Cleaning project..." -ForegroundColor Green
  dotnet clean .\HRPeripheral.csproj
  if (Test-Path ".\bin") { Remove-Item -Recurse -Force ".\bin" -ErrorAction SilentlyContinue }
  if (Test-Path ".\obj") { Remove-Item -Recurse -Force ".\obj" -ErrorAction SilentlyContinue }
  Write-Host "✅ Clean complete." -ForegroundColor Green
}

function Build-APK($cfg, [string]$Framework) {
  Write-Host "==> Restoring & publishing APK ($($cfg.Configuration))..." -ForegroundColor Green
  $extra = @("/p:AndroidPackageFormat=apk","-m:1")
  if ($cfg.VersionSuffix) { $extra += "/p:VersionSuffix=$($cfg.VersionSuffix)" }

  dotnet publish .\HRPeripheral.csproj `
    -c $cfg.Configuration `
    -f $Framework `
    @extra
}

function Find-APK([string]$Configuration, [string]$Framework) {
  Write-Host "==> Locating newest APK..." -ForegroundColor Green
  $apk = Get-ChildItem -Path ".\bin\$Configuration\$Framework" -Recurse -Filter *.apk |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
  if (-not $apk) { throw "No APK found under .\bin\$Configuration\$Framework." }
  Write-Host ("     APK: " + $apk.FullName) -ForegroundColor Yellow
  return $apk
}

function Install-Launch-Logcat([string]$WatchAdb, [string]$Package, [string]$ApkPath) {
  Write-Host "==> Uninstalling old app (ignore failure if not installed)..." -ForegroundColor Green
  adb -s $WatchAdb uninstall $Package -ErrorAction SilentlyContinue | Out-Null

  Write-Host "==> Installing APK..." -ForegroundColor Green
  adb -s $WatchAdb install --no-incremental -r -t -g "$ApkPath"

  Write-Host "==> Launching app..." -ForegroundColor Green
  adb -s $WatchAdb shell monkey -p $Package -c android.intent.category.LAUNCHER 1 | Out-Null

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
    Write-Warning "PID not found. Streaming logs filtered by package."
    adb -s $WatchAdb logcat -v time | Select-String -Pattern $Package
    return
  }

  Write-Host "==> Streaming logcat for PID $AppPid (Ctrl+C to stop)..." -ForegroundColor Green
  adb -s $WatchAdb logcat --pid=$AppPid
}

# ===== MAIN =====
$cfg = Select-Configuration

:menu while ($true) {
  $verSuffixText = if ($cfg.VersionSuffix) { " ($($cfg.VersionSuffix))" } else { "" }

  Write-Host "`n================ MENU ================" -ForegroundColor Cyan
  Write-Host "[1] Build APK ($($cfg.Configuration))"
  Write-Host "[2] Deploy latest APK ($($cfg.Configuration))"
  Write-Host "[3] Build + Deploy ($($cfg.Configuration))"
  Write-Host "[4] Clean (bin/obj)"
  Write-Host "[5] Pair/Connect over Wi-Fi ADB"
  Write-Host "[6] Change configuration (current: $($cfg.Configuration)$verSuffixText)"
  Write-Host "[7] Quit"
  $opt = Read-Host "Choose an option"

  try {
    switch ($opt) {
      "1" {
        Build-APK $cfg $Framework
        $null = Find-APK $cfg.Configuration $Framework
        Write-Host "✅ Build complete." -ForegroundColor Green
      }
      "2" {
        $dev = Prompt-Device
        if ($dev.NeedsConnect) { Pair-Connect $dev.WatchAdb $dev.PairInfo }
        $apk = Find-APK $cfg.Configuration $Framework
        Install-Launch-Logcat $dev.WatchAdb $Package $apk.FullName
      }
      "3" {
        $dev = Prompt-Device
        Build-APK $cfg $Framework
        if ($dev.NeedsConnect) { Pair-Connect $dev.WatchAdb $dev.PairInfo }
        $apk = Find-APK $cfg.Configuration $Framework
        Install-Launch-Logcat $dev.WatchAdb $Package $apk.FullName
      }
      "4" { Clean-Artifacts }
      "5" {
        $dev = Prompt-Device
        Pair-Connect $dev.WatchAdb $dev.PairInfo
        Write-Host "✅ Device is connected." -ForegroundColor Green
      }
      "6" { $cfg = Select-Configuration }
      "7" { break menu }
      default { Write-Host "Invalid option." -ForegroundColor Yellow }
    }
  }
  catch {
    Write-Host "❌ $_" -ForegroundColor Red
  }
}