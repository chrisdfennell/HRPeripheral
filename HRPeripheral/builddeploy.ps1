<#  HRPeripheral Build & Deploy GUI
    - TableLayoutPanel (no pixel math)
    - Pair / Connect / Build / Install / Launch / Logcat
    - Safe STA handling (no ISE loop)
#>

# --- STA guard (skip if in ISE which is already STA) ---
$inISE = $Host.Name -like '*ISE*'
if (-not $inISE -and [Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
  $argsList = @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File', $PSCommandPath)
  Start-Process powershell -ArgumentList $argsList | Out-Null
  exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ---------- helpers ----------
function Write-Log($msg) {
  $ts = (Get-Date).ToString('HH:mm:ss')
  $logBox.AppendText("$ts  $msg`r`n")
  $logBox.ScrollToCaret()
}

function Run-Proc {
  param(
    [Parameter(Mandatory)] [string]$FilePath,
    [Parameter(Mandatory)] [string]$Arguments,
    [switch]$NoLogExitCode
  )
  $psi = [System.Diagnostics.ProcessStartInfo]::new()
  $psi.FileName = $FilePath
  $psi.Arguments = $Arguments
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.CreateNoWindow = $true

  $p = [System.Diagnostics.Process]::new()
  $p.StartInfo = $psi

  $p.add_OutputDataReceived({ param($s,$e) if ($e.Data) { Write-Log $e.Data } })
  $p.add_ErrorDataReceived({  param($s,$e) if ($e.Data) { Write-Log "ERROR: $($e.Data)" } })

  [void]$p.Start()
  $p.BeginOutputReadLine()
  $p.BeginErrorReadLine()
  $p.WaitForExit()

  if (-not $NoLogExitCode) { Write-Log ("ExitCode: {0}" -f $p.ExitCode) }
  return $p.ExitCode
}

function Ensure-Path($p) { if (-not (Test-Path $p)) { throw "Path not found: $p" } }

# ---------- form ----------
$form = New-Object Windows.Forms.Form
$form.Text = 'HRPeripheral Build & Deploy'
$form.Size = New-Object Drawing.Size(920, 700)
$form.StartPosition = 'CenterScreen'

# layout: 2 rows of controls, 1 big row for log
$layout = New-Object Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'
$layout.ColumnCount = 8             # plenty of columns for buttons
$layout.RowCount = 4
$layout.ColumnStyles.AddRange(@(
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent, 100)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize))
))
$layout.RowStyles.AddRange(@(
  (New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)),
  (New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent, 100))
))
$form.Controls.Add($layout)

# ---------- row 0: project path + browse ----------
$lblProj = New-Object Windows.Forms.Label
$lblProj.Text = 'Project Dir:'
$lblProj.AutoSize = $true
$layout.Controls.Add($lblProj, 0, 0)

$projBox = New-Object Windows.Forms.TextBox
$projBox.Dock = 'Fill'
$projBox.Text = 'C:\Users\Infan\OneDrive\Programming\C#\HRPeripheral\HRPeripheral'
$layout.Controls.Add($projBox, 1, 0)

$browseProjBtn = New-Object Windows.Forms.Button
$browseProjBtn.Text = 'Browse…'
$layout.Controls.Add($browseProjBtn, 2, 0)

# config + framework
$lblCfg = New-Object Windows.Forms.Label
$lblCfg.Text = 'Config:'
$lblCfg.AutoSize = $true
$layout.Controls.Add($lblCfg, 3, 0)

$cfgBox = New-Object Windows.Forms.ComboBox
$cfgBox.DropDownStyle = 'DropDownList'
[void]$cfgBox.Items.AddRange(@('Debug','Release'))
$cfgBox.SelectedIndex = 0
$layout.Controls.Add($cfgBox, 4, 0)

$lblFx = New-Object Windows.Forms.Label
$lblFx.Text = 'TFM:'
$lblFx.AutoSize = $true
$layout.Controls.Add($lblFx, 5, 0)

$fxBox = New-Object Windows.Forms.ComboBox
$fxBox.DropDownStyle = 'DropDownList'
[void]$fxBox.Items.Add('net9.0-android')
$fxBox.SelectedIndex = 0
$layout.Controls.Add($fxBox, 6, 0)

# ---------- row 1: device ip/ports + pair/connect ----------
$lblIp = New-Object Windows.Forms.Label
$lblIp.Text = 'Device IP:'
$lblIp.AutoSize = $true
$layout.Controls.Add($lblIp, 0, 1)

$ipBox = New-Object Windows.Forms.TextBox
$ipBox.Width = 130
$ipBox.Text = '192.168.86.28'
$layout.Controls.Add($ipBox, 1, 1)

$lblPort = New-Object Windows.Forms.Label
$lblPort.Text = 'ADB Port:'
$lblPort.AutoSize = $true
$layout.Controls.Add($lblPort, 2, 1)

$portBox = New-Object Windows.Forms.TextBox
$portBox.Width = 70
$portBox.Text = '37467'
$layout.Controls.Add($portBox, 3, 1)

$lblPair = New-Object Windows.Forms.Label
$lblPair.Text = 'Pair Port:'
$lblPair.AutoSize = $true
$layout.Controls.Add($lblPair, 4, 1)

$pairPortBox = New-Object Windows.Forms.TextBox
$pairPortBox.Width = 70
$pairPortBox.Text = '39273'
$layout.Controls.Add($pairPortBox, 5, 1)

$pairBtn = New-Object Windows.Forms.Button
$pairBtn.Text = 'Pair'
$layout.Controls.Add($pairBtn, 6, 1)

$connectBtn = New-Object Windows.Forms.Button
$connectBtn.Text = 'Connect'
$layout.Controls.Add($connectBtn, 7, 1)

# ---------- row 2: actions + apk path ----------
$buildBtn = New-Object Windows.Forms.Button
$buildBtn.Text = 'Build APK'
$layout.Controls.Add($buildBtn, 0, 2)

$findApkBtn = New-Object Windows.Forms.Button
$findApkBtn.Text = 'Find APK'
$layout.Controls.Add($findApkBtn, 1, 2)

$lblApk = New-Object Windows.Forms.Label
$lblApk.Text = 'APK:'
$lblApk.AutoSize = $true
$layout.Controls.Add($lblApk, 2, 2)

$apkPathBox = New-Object Windows.Forms.TextBox
$apkPathBox.Dock = 'Fill'
$layout.Controls.Add($apkPathBox, 3, 2)
$layout.SetColumnSpan($apkPathBox, 3)

$browseApkBtn = New-Object Windows.Forms.Button
$browseApkBtn.Text = 'Browse…'
$layout.Controls.Add($browseApkBtn, 6, 2)

$installBtn = New-Object Windows.Forms.Button
$installBtn.Text = 'Install'
$layout.Controls.Add($installBtn, 0, 3)

$launchBtn = New-Object Windows.Forms.Button
$launchBtn.Text = 'Launch'
$layout.Controls.Add($launchBtn, 1, 3)

$logBtn = New-Object Windows.Forms.Button
$logBtn.Text = 'Logcat'
$layout.Controls.Add($logBtn, 2, 3)

$stopLogBtn = New-Object Windows.Forms.Button
$stopLogBtn.Text = 'Stop Log'
$layout.Controls.Add($stopLogBtn, 3, 3)

$clearBtn = New-Object Windows.Forms.Button
$clearBtn.Text = 'Clear'
$layout.Controls.Add($clearBtn, 4, 3)

# package + activity (for launch)
$lblPkg = New-Object Windows.Forms.Label
$lblPkg.Text = 'Package:'
$lblPkg.AutoSize = $true
$layout.Controls.Add($lblPkg, 5, 3)

$pkgBox = New-Object Windows.Forms.TextBox
$pkgBox.Width = 210
$pkgBox.Text = 'com.fennell.hrperipheral'
$layout.Controls.Add($pkgBox, 6, 3)

$lblAct = New-Object Windows.Forms.Label
$lblAct.Text = 'Activity:'
$lblAct.AutoSize = $true
$layout.Controls.Add($lblAct, 0, 4)  # this row will expand with log; we’ll add before the log

$actBox = New-Object Windows.Forms.TextBox
$actBox.Width = 300
$actBox.Text = 'crc64e341e0a65a6014c6.MainActivity'
$layout.Controls.Add($actBox, 1, 4)
$layout.SetColumnSpan($actBox, 3)

# ---------- row last: log ----------
$logBox = New-Object Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = 'Vertical'
$logBox.ReadOnly = $true
$logBox.Dock = 'Fill'
$logBox.Font = New-Object Drawing.Font('Consolas', 9)
# Place the log occupying the remaining area
$layout.Controls.Add($logBox, 0, 5)
$layout.SetColumnSpan($logBox, 8)
$layout.SetRowSpan($logBox, 1)

# ---------- state ----------
$global:LogcatProc = $null

function Get-DeviceSpec {
  param([switch]$Pair)
  $ip = ($ipBox.Text).Trim()
  $port = if ($Pair) { ($pairPortBox.Text).Trim() } else { ($portBox.Text).Trim() }
  if (-not $ip)   { throw "Device IP required." }
  if (-not $port) { throw "Port required." }
  return "$ip`:$port"
}

# ---------- event wiring ----------
$browseProjBtn.Add_Click({
  $dlg = New-Object Windows.Forms.FolderBrowserDialog
  $dlg.Description = 'Select project directory that contains *.csproj'
  if ($dlg.ShowDialog() -eq 'OK') { $projBox.Text = $dlg.SelectedPath }
})

$browseApkBtn.Add_Click({
  $dlg = New-Object Windows.Forms.OpenFileDialog
  $dlg.Filter = 'APK (*.apk)|*.apk'
  if ($dlg.ShowDialog() -eq 'OK') { $apkPathBox.Text = $dlg.FileName }
})

$pairBtn.Add_Click({
  try {
    $target = Get-DeviceSpec -Pair
    Write-Log "> adb pair $target"
    [void](Run-Proc -FilePath 'adb' -Arguments "pair $target")
    Write-Log "If prompted on watch, enter the pairing code."
  } catch { Write-Log "ERROR: $_" }
})

$connectBtn.Add_Click({
  try {
    $target = Get-DeviceSpec
    Write-Log "> adb connect $target"
    [void](Run-Proc -FilePath 'adb' -Arguments "connect $target")
    Write-Log "> adb devices"
    [void](Run-Proc -FilePath 'adb' -Arguments "devices" -NoLogExitCode)
  } catch { Write-Log "ERROR: $_" }
})

$buildBtn.Add_Click({
  try {
    $proj = $projBox.Text.Trim()
    Ensure-Path $proj
    # Build
    $cfg = $cfgBox.Text
    $tfm = $fxBox.Text
    Write-Log "==> Build ($cfg / $tfm)"
    [void](Run-Proc -FilePath 'dotnet' -Arguments "build `"$proj`" -c $cfg -t:PackageForAndroid -m:1")
    Write-Log "==> Build finished."

  } catch { Write-Log "ERROR: $_" }
})

$findApkBtn.Add_Click({
  try {
    $proj = $projBox.Text.Trim()
    $cfg = $cfgBox.Text
    $tfm = $fxBox.Text
    $apkDir = Join-Path $proj "bin\$cfg\$tfm"
    Ensure-Path $apkDir
    $apk = Get-ChildItem -Path $apkDir -Filter *.apk -ErrorAction Stop | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($apk) {
      $apkPathBox.Text = $apk.FullName
      Write-Log "APK: $($apk.FullName)"
    } else {
      Write-Log "No APK found in $apkDir"
    }
  } catch { Write-Log "ERROR: $_" }
})

$installBtn.Add_Click({
  try {
    $apk = $apkPathBox.Text.Trim()
    Ensure-Path $apk
    $target = Get-DeviceSpec
    # uninstall silently first (ignore error)
    $pkg = $pkgBox.Text.Trim()
    if ($pkg) {
      Write-Log "> adb -s $target uninstall $pkg"
      [void](Run-Proc -FilePath 'adb' -Arguments "-s $target uninstall $pkg" -NoLogExitCode)
    }
    Write-Log "> adb -s $target install --no-incremental -r -t -g `"$apk`""
    [void](Run-Proc -FilePath 'adb' -Arguments "-s $target install --no-incremental -r -t -g `"$apk`"")
  } catch { Write-Log "ERROR: $_" }
})

$launchBtn.Add_Click({
  try {
    $target = Get-DeviceSpec
    $pkg = $pkgBox.Text.Trim()
    $act = $actBox.Text.Trim()
    if (-not $pkg -or -not $act) { throw "Package and Activity required." }
    $cmp = "$pkg/$act"
    Write-Log "> adb -s $target shell am start -n $cmp"
    [void](Run-Proc -FilePath 'adb' -Arguments "-s $target shell am start -n $cmp")
  } catch { Write-Log "ERROR: $_" }
})

$logBtn.Add_Click({
  try {
    if ($global:LogcatProc -and -not $global:LogcatProc.HasExited) {
      Write-Log "Logcat already running."
      return
    }
    $target = Get-DeviceSpec
    $pkg = $pkgBox.Text.Trim()
    # get PID of app (if running); if not, stream app tag + ActivityManager as fallback
    $pid = (& adb -s $target shell pidof $pkg).Trim()
    $args = if ($pid) { "-s $target logcat --pid=$pid" } else { "-s $target logcat -v time $($pkg):D ActivityManager:I *:S" }

    Write-Log "> adb $args"
    # spawn and keep handle to stop later
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'adb'
    $psi.Arguments = $args
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.CreateNoWindow = $true

    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    $p.add_OutputDataReceived({ param($s,$e) if ($e.Data) { Write-Log $e.Data } })
    $p.add_ErrorDataReceived({  param($s,$e) if ($e.Data) { Write-Log "ERROR: $($e.Data)" } })
    [void]$p.Start()
    $p.BeginOutputReadLine()
    $p.BeginErrorReadLine()
    $global:LogcatProc = $p
  } catch { Write-Log "ERROR: $_" }
})

$stopLogBtn.Add_Click({
  try {
    if ($global:LogcatProc -and -not $global:LogcatProc.HasExited) {
      Write-Log "Stopping logcat..."
      $global:LogcatProc.Kill()
      $global:LogcatProc.Dispose()
    }
    $global:LogcatProc = $null
  } catch { Write-Log "ERROR: $_" }
})

$clearBtn.Add_Click({ $logBox.Clear() })

# default info
Write-Log "Ready. Pair or Connect, then Build → Find APK → Install → Launch → Logcat."

# show
[void]$form.ShowDialog()