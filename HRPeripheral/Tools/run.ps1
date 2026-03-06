<# =======================
 run.ps1 — HRPeripheral tool
-------------------------
Menu launcher for common tasks:
  1) Build APK
  2) Deploy latest APK (no build)
  3) Build + Deploy
  4) Clean (bin/obj)
  5) Pair/Connect over Wi-Fi ADB
  6) Change configuration
  7) Quit
  8) Build unsigned AAB (Release)
  9) Sign an AAB with keystore
 10) Build + Sign AAB (Release)

- Asks for Debug or Release Candidate (RC) at start.
- Works when launched from /tools (auto cd to project root).
- Uses /tools/bump-version.ps1 for auto-versioning (APK/AAB).
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
    "2" { return @{ Configuration = "Release"; VersionSuffix = "rc"  } }
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
  Write-Host "[OK] Clean complete." -ForegroundColor Green
}

function Bump-Version-And-Args($cfg) {
  Write-Host "==> Bumping version..." -ForegroundColor Green
  $ver = & "$PSScriptRoot\bump-version.ps1" -Patch
  if ($LASTEXITCODE -ne 0 -or -not $ver) { throw "Version bump failed. If this persists, try menu option [4] Clean (bin/obj) and rerun." }

  $displayWithSuffix = if ($cfg.VersionSuffix) { "$($ver.Display)-$($cfg.VersionSuffix)" } else { $ver.Display }
  
  # Add explicit logging to make the version clear
  Write-Host "    Display Version: $displayWithSuffix" -ForegroundColor Yellow
  Write-Host "    Version Code   : $($ver.Code)" -ForegroundColor Yellow

  $extra = @(
    "/p:ApplicationDisplayVersion=$displayWithSuffix",
    "/p:ApplicationVersion=$($ver.Code)",
    "-m:1"
  )
  if ($cfg.VersionSuffix) { $extra += "/p:VersionSuffix=$($cfg.VersionSuffix)" }

  return @{ Extra=$extra; Version=$ver; Display=$displayWithSuffix }
}

function Build-APK($cfg, [string]$Framework) {
  $v = Bump-Version-And-Args $cfg
  Write-Host "==> Restoring & publishing APK ($($cfg.Configuration))..." -ForegroundColor Green
  dotnet publish .\HRPeripheral.csproj `
    -c $cfg.Configuration `
    -f $Framework `
    /p:AndroidPackageFormat=apk `
    @($v.Extra)
}

function Build-AAB-Unsigned([string]$Framework, [string]$Package) {
  # Force Release for AAB
  $cfg = @{ Configuration = "Release"; VersionSuffix = $null }
  $v = Bump-Version-And-Args $cfg

  Write-Host "==> Restoring & publishing AAB (Release, unsigned)..." -ForegroundColor Green
  
  # [FIX] Pipe build output to Out-Null. This prevents the build log from being
  # captured by the function's return value and ensures the script waits for the
  # build to complete before continuing.
  dotnet publish .\HRPeripheral.csproj `
    -c Release `
    -f $Framework `
    /p:AndroidPackageFormat=aab `
    /p:AndroidSigning=false `
    @($v.Extra) | Out-Null
  
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

  Write-Host "==> Renaming output AAB to include version..." -ForegroundColor Green
  $publishPath = ".\bin\Release\$Framework\publish"
  
  # Find the generated AAB. It might be named com.fennell.hrperipheral.aab or com.fennell.hrperipheral-unsigned.aab
  $sourceAab = Get-ChildItem -Path $publishPath -Recurse -Filter "*.aab" |
    Where-Object { $_.Name -notlike "*-signed*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

  if (-not $sourceAab) { throw "Could not find the newly built unsigned AAB in '$publishPath'." }
  
  # Create a new name that includes version info for clarity
  $newName = "$Package-$($v.Display)-$($v.Version.Code)-unsigned.aab"
  $newPath = Join-Path -Path $sourceAab.DirectoryName -ChildPath $newName
  
  # Rename the file, removing the destination if it exists
  if (Test-Path $newPath) { Remove-Item $newPath }
  Rename-Item -Path $sourceAab.FullName -NewName $newName
  
  Write-Host "    Renamed to: $newPath" -ForegroundColor Yellow
  return $newPath
}

function Find-APK([string]$Configuration, [string]$Framework) {
  Write-Host "==> Locating newest APK..." -ForegroundColor Green
  # Search in the 'publish' subfolder for better accuracy
  $apk = Get-ChildItem -Path ".\bin\$Configuration\$Framework\publish" -Recurse -Filter *.apk |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
  if (-not $apk) {
    throw "No APK found under .\bin\$Configuration\$Framework\publish. If you just changed configurations or rebuilt, try menu option [4] Clean (bin/obj) and run again."
  }
  Write-Host ("      APK: " + $apk.FullName) -ForegroundColor Yellow
  return $apk
}

function Find-AAB([string]$Configuration, [string]$Framework) {
  Write-Host "==> Locating newest UNsigned AAB..." -ForegroundColor Green
  $publishPath = ".\bin\$Configuration\$Framework\publish"

  # Find all .aab files and explicitly exclude any that are already signed.
  # Prefer files with the "-unsigned" suffix from our new build process.
  $aab = Get-ChildItem -Path $publishPath -Recurse -Filter "*-unsigned.aab" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
    
  if (-not $aab) {
    # Fallback for old file name, just in case
    $aab = Get-ChildItem -Path $publishPath -Recurse -Filter "*.aab" |
      Where-Object { $_.Name -notlike "*-signed*" } |
      Sort-Object LastWriteTime -Descending |
      Select-Object -First 1
  }

  if (-not $aab) {
    throw "No unsigned AAB found in '$publishPath'. Please build one first using option [8]."
  }
  Write-Host ("      AAB: " + $aab.FullName) -ForegroundColor Yellow
  return $aab
}

function Ensure-JarSigner {
  $js = Get-Command jarsigner -ErrorAction SilentlyContinue
  if (-not $js) {
    throw "jarsigner not found. Ensure your JDK bin folder is in your PATH environment variable (e.g. C:\Program Files\Microsoft\jdk-17\bin) or open a Developer PowerShell where the JDK is available."
  }
}

function Prompt-Keystore {
  Write-Host "`n--- Keystore Settings ---" -ForegroundColor Cyan
  $KeystorePath = Read-Host "Keystore path (default C:\MyKeys\my-upload-key.keystore)"
  if ([string]::IsNullOrWhiteSpace($KeystorePath)) { $KeystorePath = "C:\MyKeys\my-upload-key.keystore" }
  if (-not (Test-Path $KeystorePath)) { throw "Keystore not found at: $KeystorePath" }

  $Alias = Read-Host "Key alias (default my-key-alias)"
  if ([string]::IsNullOrWhiteSpace($Alias)) { $Alias = "my-key-alias" }

  # Secure password prompts
  $StorePass = Read-Host "Keystore password" -AsSecureString
  $KeyPass   = Read-Host "Key password (press Enter to reuse keystore password)" -AsSecureString

  if (-not $KeyPass.Length) { $KeyPass = $StorePass }

  # Convert to plain for jarsigner (jarsigner has no way to read SecureString directly)
  $BSTR1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($StorePass)
  $BSTR2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($KeyPass)
  try {
    $StorePassPlain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($BSTR1)
    $KeyPassPlain   = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($BSTR2)
  } finally {
    if ($BSTR1 -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR1) }
    if ($BSTR2 -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR2) }
  }

  return @{
    KeystorePath = $KeystorePath
    Alias        = $Alias
    StorePass    = $StorePassPlain
    KeyPass      = $KeyPassPlain
  }
}

function Sign-AAB([string]$UnsignedAabPath, $ks) {
  Ensure-JarSigner
  if (-not (Test-Path $UnsignedAabPath)) { throw "Unsigned AAB not found: $UnsignedAabPath" }

  # More robustly determine the output path
  $aabFile = Get-Item $UnsignedAabPath
  $baseName = $aabFile.BaseName.Replace("-unsigned", "") # Remove -unsigned if it exists
  $directory = $aabFile.DirectoryName
  $signedAabPath = Join-Path -Path $directory -ChildPath "$baseName-signed-release.aab"

  Write-Host "==> Preparing to sign. Output file will be: `n    $signedAabPath" -ForegroundColor Magenta
  Copy-Item -Path $UnsignedAabPath -Destination $signedAabPath -Force

  Write-Host "==> Signing AAB with jarsigner..." -ForegroundColor Green
  # We will sign the newly copied file
  & jarsigner `
    -keystore "$($ks.KeystorePath)" `
    -storepass "$($ks.StorePass)" `
    -keypass "$($ks.KeyPass)" `
    -sigalg SHA256withRSA `
    -digestalg SHA-256 `
    "$signedAabPath" `
    "$($ks.Alias)"

  if ($LASTEXITCODE -ne 0) { throw "jarsigner failed. Please double-check that your alias ('$($ks.Alias)') and passwords are correct and exist in the keystore." }

  Write-Host "==> Verifying signature..." -ForegroundColor Green
  & jarsigner -verify -verbose -certs "$signedAabPath"
  if ($LASTEXITCODE -ne 0) { throw "Signature verification failed." }

  Write-Host "[OK] AAB signed and verified: $signedAabPath" -ForegroundColor Green
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
    Write-Host "      (retrying...)"
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
  Write-Host "[8] Build unsigned AAB (Release)"
  Write-Host "[9] Sign an AAB with keystore"
  Write-Host "[10] Build + Sign AAB (Release)"
  $opt = Read-Host "Choose an option"

  try {
    switch ($opt) {
      "1" {
        Build-APK $cfg $Framework
        $null = Find-APK $cfg.Configuration $Framework
        Write-Host "[OK] Build complete." -ForegroundColor Green
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
        Write-Host "[OK] Device is connected." -ForegroundColor Green
      }
      "6" { $cfg = Select-Configuration }
      "7" { break menu }
      "8" {
        $null = Build-AAB-Unsigned $Framework $Package
        Write-Host "[OK] Unsigned AAB build complete." -ForegroundColor Green
      }
      "9" {
        $aab = Find-AAB "Release" $Framework
        $ks  = Prompt-Keystore
        Sign-AAB $aab.FullName $ks
      }
      "10" {
        $unsignedAabPath = Build-AAB-Unsigned $Framework $Package
        $ks  = Prompt-Keystore
        Sign-AAB $unsignedAabPath $ks
        Write-Host "[OK] Build + Sign AAB completed." -ForegroundColor Green
      }
      default { Write-Host "Invalid option." -ForegroundColor Yellow }
    }
  }
  catch {
    Write-Host "[ERROR] $_" -ForegroundColor Red
    Write-Host "Hint: If you switched build types or recently updated .NET/SDKs, run menu option [4] Clean (bin/obj) and try again." -ForegroundColor Yellow
  }
}