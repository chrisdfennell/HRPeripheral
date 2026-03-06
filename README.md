[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/Language-C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Platform](https://img.shields.io/badge/Platform-Wear%20OS-4285F4?logo=wear-os&logoColor=white)](https://wearos.google.com/)
[![Target](https://img.shields.io/badge/Target-Android%20API%2023+-3DDC84?logo=android&logoColor=white)](#)
![Sensors](https://img.shields.io/badge/Sensors-Heart%20Rate-red)
[![Bluetooth](https://img.shields.io/badge/BLE-Peripheral%20%2B%20Central-blue)](#)
[![Tests](https://img.shields.io/badge/Tests-342%20passing-brightgreen)](#-testing)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#-license)

# 🩺 HRPeripheral — Wear OS Heart Rate Peripheral

> ⚙️ A minimal Wear OS app that turns your watch into a Bluetooth LE **Heart Rate (HR) peripheral**.
> 📡 It advertises the standard **Heart Rate Service (0x180D)** and streams **Heart Rate Measurement (0x2A37)** notifications.
> 📱 Includes a **Companion app** that connects as a BLE Central to display live HR, zones, calories, and session stats.

---

<p align="center">
  <a href="#quick-start">Quick Start</a> ·
  <a href="#developer-setup">Developer Setup</a> ·
  <a href="#building--deploying">Building & Deploying</a> ·
  <a href="#wireless-adb-setup-wear-os">Wireless ADB</a> ·
  <a href="#signing--aab-builds">Signing & AAB Builds</a> ·
  <a href="#testing">Testing</a> ·
  <a href="#common-errors--fixes">Common Errors & Fixes</a> ·
  <a href="#play-console-wear-os-release-guide">Play Console Guide</a> ·
  <a href="#script-tips-toolsrunps1">Script Tips</a>
</p>

---

## 📸 Screenshots

<p align="center">
  <img src="https://raw.githubusercontent.com/chrisdfennell/HRPeripheral/master/images/Screenshot%20-%20Main.png" width="260" alt="Main Screen">
  <img src="https://raw.githubusercontent.com/chrisdfennell/HRPeripheral/master/images/Screenshot%20-%20Settings%201.png" width="260" alt="Settings 1">
  <br>
  <img src="https://raw.githubusercontent.com/chrisdfennell/HRPeripheral/master/images/Screenshot%20-%20Settings%202.png" width="260" alt="Settings 2">
  <img src="https://raw.githubusercontent.com/chrisdfennell/HRPeripheral/master/images/Screenshot%20-%20Closing.png" width="260" alt="Closing Screen">
</p>

---

## ✨ Features

### HRPeripheral (Wear OS Watch App)
- 💓 BLE Peripheral (GATT server) with **Heart Rate Service** and **Battery Service**
- 📈 Full‑screen HR graph with live BPM overlay (top‑right)
- ⏸️ Auto‑pause via accelerometer (configurable still‑window and watchdog timings)
- ⏱️ "Press & hold to exit" with configurable hold time (Settings)
- 🧼 Optional **Forget devices** utilities (Settings) to clear bonds
- 🧭 Scriptable build/deploy via `tools/run.ps1`

### HRPeripheral.Companion (Android Phone App)
- 📱 BLE Central that connects to any **Heart Rate peripheral** (watches, chest straps, etc.)
- 📊 Live HR display with **heart rate zone** tracking (5‑zone model)
- 🔥 Real‑time **calorie estimation** (ACSM regression, configurable profile)
- 📉 HR graph and **zone time breakdown** with color‑coded bars
- 🔋 Battery level monitoring from the peripheral
- 🔄 Automatic reconnection with exponential backoff

---

## Quick Start

1. **Clone repo**
   ```bash
   git clone https://github.com/chrisdfennell/HRPeripheral.git
   cd HRPeripheral
   ```

2. **Open in Visual Studio / VS Code** (Android workload installed).

3. **Enable Developer Options on the watch**
   - On the watch: **Settings → System → About → Build number** (tap 7 times)
   - Go back to **Settings → Developer options**
     - Turn **Developer options** ON
     - Enable **ADB debugging**
     - (Optional) Enable **Debug over Wi‑Fi** (for wireless deployment)

4. **Connect** (USB, or Wi‑Fi ADB — see section below).

5. **Build & deploy** using the helper script:
   ```powershell
   cd tools
   .\run.ps1
   ```
   - Choose **Debug** or **Release Candidate**
   - Use menu **[3] Build + Deploy**

---

## Developer Setup

### Prereqs
- 🪟 Windows 10/11
- **Visual Studio 2022** (or VS Code) with **.NET** + **Android** workload
- **.NET SDK 10** (or update `Framework` in `tools/run.ps1` to match your SDK)
- **ADB** in PATH (installed with Android SDK)

### First build tips
- If you hit build errors after switching branches/SDKs, do a **Clean**:
  - `tools/run.ps1` → **[4] Clean (bin/obj)**
  - Then rebuild/deploy.

---

## Building & Deploying

### Using the menu script (recommended)
```powershell
cd tools
.\run.ps1
```
- **[1] Build APK** — compiles & bumps version
- **[2] Deploy latest APK** — installs most recent `bin/<Config>/<TFM>/*.apk`
- **[3] Build + Deploy** — does both
- **[4] Clean (bin/obj)** — deletes `bin/` and `obj/`
- **[5] Pair/Connect over Wi‑Fi ADB** — helpers for pairing/connecting
- **[6] Change configuration** — switch Debug/Release Candidate
- **[8] Build unsigned AAB (Release)** — creates Play bundle
- **[9] Sign AAB** — sign an unsigned `.aab` using your keystore
- **[10] Build + Sign AAB** — end‑to‑end for Play upload

> If **"No APK found under .\\bin\\<Config>\\<TFM>"**: run **[4] Clean (bin/obj)**, then **[1] Build** again.

### Manual build (Visual Studio)
- Set **Startup Project** to `HRPeripheral`
- Select target **Wear OS device** (emulator or physical)
- **Run** (▶) to install & launch

---

## Wireless ADB Setup (Wear OS)

> Works on Wear OS 3+ (Samsung, Pixel Watch, etc.) and most Wear OS 2 devices.

1. **Enable Developer options** (see Quick Start).
2. In **Developer options**:
   - Turn on **ADB debugging**
   - Turn on **Debug over Wi‑Fi**
3. Note the **IP address & port** on the watch (e.g., `192.168.86.28:44139`).
4. On your PC:
   ```powershell
   adb connect 192.168.86.28:44139
   adb devices    # should show the watch as 'device'
   ```
5. If pairing is required:
   - On watch: **Pair with watch** → shows **pairing code** + **pairing port** (different from ADB port).
   - On PC:
     ```powershell
     adb pair 192.168.86.28:12345 000000
     adb connect 192.168.86.28:44139
     ```
6. Or use `tools/run.ps1` → **[5] Pair/Connect over Wi‑Fi ADB** for guided prompts.

---

## App Behavior / Controls

- **Graph:** live HR plot; BPM shown at top‑right inside the graph.
- **Settings (gear icon):**
  - Toggle **Press & Hold to Exit**
  - Adjust **Hold Duration** (5–15s)
  - Toggle **Auto‑Pause** (pauses HR broadcasting when still)
  - Configure **calorie profile** (sex, weight, age)
  - **Forget Devices** (clears known BLE centrals / bonds) — optional utility

---

## Signing & AAB Builds

> For Play Store uploads you need a **signed `.aab`** (Android App Bundle).
> Use `tools/run.ps1` menu **[8–10]** for automation, or follow the manual steps below.

### Build unsigned AAB (Release)
```powershell
dotnet publish .\HRPeripheral.csproj -c Release -f net10.0-android `
  /p:AndroidPackageFormat=aab `
  /p:ApplicationDisplayVersion=1.0.0 `
  /p:ApplicationVersion=1
```
The output `.aab` will be under:
`HRPeripheral\bin\Release\net10.0-android\publish\`

### Sign AAB (manual example)
```powershell
cd "HRPeripheral\bin\Release\net10.0-android\publish"

jarsigner -verbose `
  -sigalg SHA256withRSA -digestalg SHA-256 `
  -keystore "path\to\my-upload-key.keystore" `
  -storepass <storepass> -keypass <keypass> `
  "HRPeripheral.aab" upload

jarsigner -verify -verbose -certs HRPeripheral.aab
```
> Replace `<storepass>` / `<keypass>` with your credentials. The key alias above is `upload` (change if yours differs).

---

## Testing

The shared library has a comprehensive xUnit test suite covering all edge cases:

```powershell
dotnet test HRPeripheral.Tests/
```

**342 tests** across 7 test classes:

| Test Class | Tests | Covers |
|---|---|---|
| `CalorieEstimatorTests` | 21 | Male/female formulas, extreme HR/weight/age values, monotonicity |
| `HrPayloadTests` | 25 | BLE payload build/parse, UInt8/UInt16 formats, clamping, round-trips |
| `HrpPrefsTests` | 45 | All clamp functions, boundary values, idempotency, constants consistency |
| `HeartRateZoneTests` | 42 | Zone boundaries, age clamping, BPM ranges, colors, zone array structure |
| `ReconnectBackoffTests` | 22 | Exponential doubling, cap behavior, reset, attempt tracking |
| `SessionTrackerTests` | 34 | Min/max/avg stats, calorie accumulation, zone tracking, reset, formatting |
| `ZoneTimeTrackerTests` | 23 | Zone transitions, gap handling, sub-second precision, boundary conditions |

---

## Common Errors & Fixes

### 1) "Version bump failed" (tools/run.ps1)
- Usually because `bin/` / `obj/` contain stale artifacts.
- **Fix:** In `tools/run.ps1` choose **[4] Clean (bin/obj)**, then build again.

### 2) "No APK found under .\\bin\\<Config>\\<TFM>"
- Build didn't emit an APK (or wrong configuration/path).
- **Fix:** Run **[4] Clean**, then **[1] Build**. Make sure the `Framework` in `tools/run.ps1` matches your SDK (e.g., `net9.0-android` vs `net10.0-android`).

### 3) ADB not connecting
- Verify the watch and PC are on the **same Wi‑Fi**.
- If pairing is needed, use `adb pair ip:pairPort code` **before** `adb connect ip:adbPort`.
- Re‑toggle **ADB debugging** on the watch if it gets stuck.

### 4) Play Console: "This APK or bundle requires the Wear OS system feature android.hardware.type.watch…"
- You uploaded a **Wear‑only** build to the **Phone** track.
- **Fix:** Use the **Wear OS** form factor release track (see guide below).

---

## Play Console Wear OS Release Guide

This app is **Wear OS only**. Use the Wear form factor release flow and ensure the manifest declares the watch feature.

### A) Manifest requirements (AndroidManifest.xml)
```xml
<manifest ...>
  <!-- Required: target only Wear OS -->
  <uses-feature android:name="android.hardware.type.watch" android:required="true" />

  <!-- Strongly recommended for BLE apps -->
  <uses-feature android:name="android.hardware.bluetooth_le" android:required="true" />
  <!-- If you read HR from sensors locally (optional for peripheral mode) -->
  <uses-feature android:name="android.hardware.sensor.heartrate" android:required="false" />

  <!-- Optional to avoid Play thinking you require a touchscreen -->
  <uses-feature android:name="android.hardware.touchscreen" android:required="false" />
</manifest>
```

If you keep your own `AndroidManifest.xml`, place these `<uses-feature>` entries there.
If you rely on MSBuild to inject, verify the **Merged Manifest** (Build output → `obj/.../android/AndroidManifest.xml`) contains them.

### B) Create a Wear OS listing & upload to the **Wear OS** track
1. **All apps → Your app → Production → Create new release**
2. At the top, choose **Form factor: Wear OS** (do **not** use Phone).
3. Complete the **Store listing → Wear OS** section (Wear screenshots, icon, short/full descriptions).
4. Upload your **signed `.aab`** built from this project.
5. Resolve policy checks & roll out.

If you see the error:
> "This APK or bundle requires the Wear OS system feature android.hardware.type.watch. To publish this release on the current track, remove this artifact."
You tried to upload to the **Phone** track. Switch the form factor to **Wear OS**.

### C) Optional: exclude phones entirely
The `<uses-feature android.hardware.type.watch android:required="true" />` already restricts installs to Wear. No extra `<supports-screens>` tweaks are needed.

---

## Project Structure

```
HRPeripheral/                  # Wear OS watch app (BLE Peripheral)
  ├─ Views/
  │   └─ HrGraphView.cs        # Live HR graph custom view
  ├─ HeartRateService.cs        # Foreground service, sensor reading, auto-pause
  ├─ BlePeripheral.cs           # GATT server (HR + Battery services)
  ├─ MainActivity.cs            # Main UI, settings menu
  ├─ LocalBroadcastManager.cs   # In-process broadcast helper
  ├─ Resources/
  │   └─ layout/
  │       └─ activity_main.xml
  └─ tools/
      ├─ run.ps1
      └─ bump-version.ps1

HRPeripheral.Companion/        # Android phone app (BLE Central)
  ├─ BleCentralService.cs       # Foreground service, GATT client, reconnect
  ├─ BleScanner.cs              # BLE scanner for HR peripherals
  ├─ CompanionMainActivity.cs   # Main UI, HR display, zones, calories
  ├─ CompanionHrGraphView.cs    # HR graph for companion
  └─ Resources/

HRPeripheral.Shared/           # Shared library (pure C#, no Android deps)
  ├─ CalorieEstimator.cs        # ACSM calorie estimation from HR
  ├─ HeartRateZone.cs           # 5-zone HR model (50-100% max HR)
  ├─ HrPayload.cs               # BLE HR Measurement payload build/parse
  ├─ HrpPrefs.cs                # Preference keys, defaults, validation
  ├─ ReconnectBackoff.cs        # Exponential backoff for BLE reconnect
  ├─ SessionTracker.cs          # Session stats (min/max/avg, calories, duration)
  └─ ZoneTimeTracker.cs         # Cumulative time in each HR zone

HRPeripheral.Tests/            # xUnit test suite (342 tests)
  ├─ CalorieEstimatorTests.cs
  ├─ HeartRateZoneTests.cs
  ├─ HrPayloadTests.cs
  ├─ HrpPrefsTests.cs
  ├─ ReconnectBackoffTests.cs
  ├─ SessionTrackerTests.cs
  └─ ZoneTimeTrackerTests.cs
```

---

## Script Tips (`tools/run.ps1`)

- To change target framework:
  - Run with `-Framework net10.0-android` (etc.), or edit default in the script param.
- If you use **Release Candidate**, the script appends `-rc` to `ApplicationDisplayVersion` for you.
- When deploying, the script will:
  1. Uninstall the old app (ignores failure if not found)
  2. Install the new APK
  3. Launch the app
  4. Stream **logcat** filtered to the app's PID
- If you hit **"Version bump failed."** right after a dotnet publish, try menu **[4] Clean** and rebuild.

---

## License

MIT
