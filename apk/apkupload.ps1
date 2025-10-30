param(
  [string]$ApkPath = '.\hrper.apk',
  [string]$AdbPath = '',
  [switch]$ForceConnect
)

function Find-Adb {
  param([string]$Explicit)
  if ($Explicit -and (Test-Path $Explicit)) { return (Resolve-Path $Explicit).ProviderPath }
  $cmd = Get-Command adb -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  foreach ($p in @(
    "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
    "$env:ProgramFiles\Android\platform-tools\adb.exe",
    "$env:ProgramFiles(x86)\Android\android-sdk\platform-tools\adb.exe",
    "$env:USERPROFILE\AppData\Local\Android\sdk\platform-tools\adb.exe",
    'C:\platform-tools\adb.exe'
  )) { if (Test-Path $p) { return (Resolve-Path $p).ProviderPath } }
  return $null
}

function Run-Adb {
  param(
    [Parameter(Mandatory)] [string]$Adb,
    [Parameter(Mandatory)] [string[]]$Args,
    [switch]$Quiet
  )
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $Adb
  $psi.Arguments = ($Args -join ' ')
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $stdout = $p.StandardOutput.ReadToEnd()
  $stderr = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  if (-not $Quiet) {
    if ($stdout.Trim()) { Write-Host $stdout.Trim() }
    if ($stderr.Trim()) { Write-Host $stderr.Trim() -ForegroundColor Yellow }
  }
  [pscustomobject]@{ ExitCode=$p.ExitCode; StdOut=$stdout; StdErr=$stderr; Command="$Adb $($Args -join ' ')" }
}

function Confirm-YesNo($Message, [bool]$Default=$true) {
  $suffix = if ($Default) { '[Y/n]' } else { '[y/N]' }
  while ($true) {
    $ans = Read-Host "$Message $suffix"
    if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
    switch ($ans.ToLowerInvariant()) {
      'y' { return $true }
      'yes' { return $true }
      'n' { return $false }
      'no' { return $false }
      default { Write-Host 'Please answer y or n.' -ForegroundColor Yellow }
    }
  }
}

# Parse ONLY valid authorized/online serials (first column where status == 'device')
function Get-DeviceSerials {
  param([string]$Adb)
  $out = (Run-Adb -Adb $Adb -Args @('devices') -Quiet).StdOut
  $lines = $out -split "`r?`n" | Where-Object { $_ -and ($_ -notmatch '^List of devices') }
  $serials = @()
  foreach ($ln in $lines) {
    $cols = $ln -split '\s+'
    if ($cols.Count -ge 2 -and $cols[1] -eq 'device') { $serials += $cols[0] }
  }
  return ,$serials  # force array
}

# Choose a serial, preferring IP:PORT if present
function Choose-Serial {
  param([string[]]$Serials, [string]$PreferIpPort)
  if ($PreferIpPort -and ($Serials -contains $PreferIpPort)) { return $PreferIpPort }
  $ipSerials = $Serials | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+:\d+$' }
  if ($ipSerials.Count -eq 1) { return $ipSerials[0] }
  if ($Serials.Count -eq 1) { return $Serials[0] }

  Write-Host ''
  Write-Host 'Multiple devices detected:' -ForegroundColor Yellow
  for ($i=0; $i -lt $Serials.Count; $i++) {
    Write-Host ("[{0}] {1}" -f ($i+1), $Serials[$i])
  }
  while ($true) {
    $choice = Read-Host ("Select device number (1-{0})" -f $Serials.Count)
    if ([int]::TryParse($choice, [ref]$null)) {
      $idx = [int]$choice
      if ($idx -ge 1 -and $idx -le $Serials.Count) { return $Serials[$idx-1] }
    }
    Write-Host 'Invalid selection.' -ForegroundColor Yellow
  }
}

Write-Host '=== Wear OS Wireless APK Uploader (auto-skip connect) ===' -ForegroundColor Cyan

$adb = Find-Adb -Explicit $AdbPath
if (-not $adb) { Write-Error 'adb.exe not found. Install Platform-Tools or provide -AdbPath.'; exit 2 }
Write-Host "Using adb: $adb`n"
Run-Adb -Adb $adb -Args @('start-server') -Quiet | Out-Null

# Detect already-connected devices (skip pairing/connect if any)
$serial = $null
$connected = $false
if (-not $ForceConnect) {
  $curr = Get-DeviceSerials -Adb $adb
  if ($curr.Count -gt 0) {
    Write-Host ('Serial candidates: ' + ($curr -join ', '))
    $serial = Choose-Serial -Serials $curr -PreferIpPort $null
    $connected = $true
    Write-Host "Detected connected device: $serial (skipping pairing/connect)" -ForegroundColor Green
  } else {
    Write-Host 'No authorized/online devices detected; will prompt for pairing/connect.' -ForegroundColor Yellow
  }
} else {
  Write-Host 'ForceConnect specified — will prompt for pairing/connect.' -ForegroundColor Yellow
}

# Pair/connect only if needed
if (-not $connected) {
  if (Confirm-YesNo 'Do you need to PAIR over Wi-Fi first? (only when watch shows pairing address and code)') {
    $pairAddr = Read-Host 'Pairing IP:PORT (example 192.168.86.52:43907)'
    if ([string]::IsNullOrWhiteSpace($pairAddr)) { Write-Error 'Pairing address required.'; exit 3 }
    $pairCode = Read-Host 'Pairing code (6 digits)'
    if ([string]::IsNullOrWhiteSpace($pairCode)) { Write-Error 'Pairing code required.'; exit 4 }
    Write-Host "Pairing with $pairAddr ..."
    $pair = Run-Adb -Adb $adb -Args @('pair', $pairAddr, $pairCode)
    if ($pair.StdOut -match 'Successfully paired' -or $pair.StdErr -match 'Successfully paired') {
      Write-Host 'Paired successfully.' -ForegroundColor Green
    } else {
      Write-Host $pair.StdOut
      Write-Host $pair.StdErr -ForegroundColor Yellow
      Write-Error 'Pairing did not report success.'; exit 5
    }
  }

  $defaultConnect = '192.168.86.52:43801'
  $connectAddr = Read-Host "ADB connect IP:PORT (default $defaultConnect)"
  if ([string]::IsNullOrWhiteSpace($connectAddr)) { $connectAddr = $defaultConnect }
  Write-Host "Connecting to $connectAddr ..."
  $connect = Run-Adb -Adb $adb -Args @('connect', $connectAddr)
  if ($connect.StdOut -notmatch 'connected to' -and $connect.StdOut -notmatch 'already connected' -and $connect.StdErr -notmatch 'connected to') {
    Write-Error "Failed to connect to $connectAddr."; exit 6
  }
  Write-Host 'Connected.' -ForegroundColor Green

  # Retry a few times for the device to appear
  $serials = @()
  for ($i=0; $i -lt 5; $i++) {
    $serials = Get-DeviceSerials -Adb $adb
    if ($serials.Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
  }
  if ($serials.Count -eq 0) { Write-Error 'No authorized/online devices after connect.'; exit 7 }
  Write-Host ('Serial candidates: ' + ($serials -join ', '))
  $serial = Choose-Serial -Serials $serials -PreferIpPort $connectAddr
}

# APK path
if (-not (Test-Path $ApkPath)) {
  $ApkPath = Read-Host 'Path to APK (default .\hrper.apk)'
  if ([string]::IsNullOrWhiteSpace($ApkPath)) { $ApkPath = '.\hrper.apk' }
}
if (-not (Test-Path $ApkPath)) { Write-Error "APK not found at '$ApkPath'."; exit 8 }
$apkFull = (Resolve-Path $ApkPath).ProviderPath

# Install flags
$replace = Confirm-YesNo 'Use -r (replace existing app)?' $true
$grant   = Confirm-YesNo 'Use -g (auto-grant runtime permissions)?' $true
$downg   = Confirm-YesNo 'Allow downgrade with -d (only if you see VERSION_DOWNGRADE errors)?' $false
$user0   = Confirm-YesNo 'Force install for user 0 (add --user 0)?' $true

$installArgs = @('-s', $serial, 'install')
if ($replace) { $installArgs += '-r' }
if ($grant)   { $installArgs += '-g' }
if ($downg)   { $installArgs += '-d' }
if ($user0)   { $installArgs += @('--user','0') }
$installArgs += "`"$apkFull`""

Write-Host "Installing APK to $serial ..."
$inst = Run-Adb -Adb $adb -Args $installArgs
if ($inst.StdOut -match '(?im)^\s*Success\s*$') {
  Write-Host 'APK installed successfully.' -ForegroundColor Green
} else {
  Write-Host $inst.StdOut
  Write-Host $inst.StdErr -ForegroundColor Yellow
  Write-Error 'adb install did not report Success.'
  if (Confirm-YesNo 'Show last 80 logcat lines to diagnose?' $true) {
    Run-Adb -Adb $adb -Args @('-s',$serial,'logcat','-d','-t','80') | Out-Null
  }
  exit 9
}

if (Confirm-YesNo 'Disconnect from the device now?' $false) {
  Run-Adb -Adb $adb -Args @('disconnect', $serial) | Out-Null
  Write-Host 'Disconnected.'
}

Write-Host 'Done.' -ForegroundColor Cyan