<# builddeploy.ps1 (fixed) #>

# ========= CONFIG =========
$Package       = "com.fennell.hrperipheral"
$WatchAdb      = "192.168.86.28:37467"   # ← your ADB IP:PORT
$PairIpPort    = ""                      # e.g. "192.168.86.28:39273" if you need pairing
$PairCode      = ""                      # e.g. "123456"
$Configuration = "Debug"
$Framework     = "net9.0-android"
# =========================

$ErrorActionPreference = "Stop"
$env:MSBUILDDISABLENODEREUSE = "1"

Write-Host "==> Ensuring clean-ish state..."
if (Test-Path ".\bin") { Remove-Item -Recurse -Force ".\bin" }
if (Test-Path ".\obj") { Remove-Item -Recurse -Force ".\obj" }

Write-Host "==> Restoring & building APK..."
dotnet build -c $Configuration -f $Framework -t:PackageForAndroid -m:1

Write-Host "==> Locating newest APK..."
$apk = Get-ChildItem -Path ".\bin\$Configuration\$Framework" -Filter *.apk |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw "No APK found under .\bin\$Configuration\$Framework" }
Write-Host ("    APK: " + $apk.FullName)

# ===== ADB PAIR/CONNECT =====
if ($PairIpPort -and $PairCode) {
  Write-Host "==> Pairing with $PairIpPort ..."
  adb pair $PairIpPort $PairCode
}

Write-Host "==> Connecting to $WatchAdb ..."
adb connect $WatchAdb | Out-Null

# Robust connection check: prefer get-state for the specific target
Write-Host "==> Verifying device state..."
$state = ""
try { $state = (adb -s $WatchAdb get-state).Trim() } catch {}
if ($state -ne "device") {
  $devicesText = ((adb devices) -join "`n")
  throw "Watch not connected (state='$state'). adb devices:`n$devicesText"
}

# ===== INSTALL =====
Write-Host "==> Uninstalling old app (ok if not installed)..."
adb -s $WatchAdb uninstall $Package | Out-Null

Write-Host "==> Installing new APK..."
adb -s $WatchAdb install --no-incremental -r -t -g "$($apk.FullName)"

# ===== LAUNCH =====
Write-Host "==> Launching app..."
adb -s $WatchAdb shell monkey -p $Package -c android.intent.category.LAUNCHER 1 | Out-Null

# ===== LOGCAT =====
Write-Host "==> Waiting for PID..."
$appPid = ""
for ($i=0; $i -lt 20; $i++) {
  $appPid = (adb -s $WatchAdb shell pidof $Package) -replace '\s',''
  if ($appPid) { break }
  Start-Sleep -Milliseconds 250
}
if (-not $appPid) {
  Write-Warning "App PID not found; printing recent crash/errors for hints..."
  adb -s $WatchAdb logcat -v time AndroidRuntime:E Mono:E ActivityManager:I ActivityTaskManager:I $Package:D *:S -d
  exit 1
}

Write-Host "==> Streaming logcat for PID $appPid ..."
adb -s $WatchAdb logcat --pid=$appPid
