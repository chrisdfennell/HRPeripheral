# HRPeripheral — Wear OS Heart Rate Peripheral

> A minimal Wear OS app that turns your watch into a Bluetooth LE **Heart Rate (HR) peripheral**.  
> It advertises the standard **Heart Rate Service (0x180D)** and streams **Heart Rate Measurement (0x2A37)** notifications.

---

<p align="center">
  <a href="#quick-start">Quick Start</a> ·
  <a href="#developer-setup">Developer Setup</a> ·
  <a href="#building--deploying">Building & Deploying</a> ·
  <a href="#wireless-adb-setup-wear-os">Wireless ADB</a> ·
  <a href="#common-errors--fixes">Common Errors & Fixes</a> ·
  <a href="#play-console-wear-os-release-guide">Play Console Guide</a>
</p>

---

## Features
- BLE Peripheral (GATT server) with **Heart Rate Service**
- Full‑screen HR graph with live BPM overlay (top‑right)
- “Press & hold to exit” with configurable hold time (Settings)
- Optional “Forget devices” utilities (Settings) to clear bonds

---

## Quick Start

1. **Clone repo**
   ```bash
   git clone https://github.com/you/hrperipheral.git
   cd hrperipheral
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
- Windows 10/11
- **Visual Studio 2022** (or VS Code) with **.NET** + **Android** workload
- **.NET SDK 9** (or update `Framework` in `tools/run.ps1` to match your SDK)
- **ADB** in PATH (installed with Android SDK)

### First build tips
- If you hit weird build errors after switching branches/SDKs, do a **Clean**:
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

> If **“No APK found under .\bin\<Config>\<TFM>”**: run **[4] Clean (bin/obj)**, then **[1] Build** again.

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
3. Note the **IP address & port** shown on the watch (e.g., `192.168.86.28:44139`).
4. On your PC:
   ```powershell
   adb connect 192.168.86.28:44139
   adb devices    # should show the watch as 'device'
   ```
5. If pairing is required (some devices):
   - In Developer options, enable **Pair with watch** → you’ll see a **pairing code** and **pairing port** (not the same as the ADB port).
   - On PC:
     ```powershell
     adb pair 192.168.86.28:12345 000000
     adb connect 192.168.86.28:44139
     ```
6. Use `tools/run.ps1` → **[5] Pair/Connect over Wi‑Fi ADB** for guided prompts.

---

## App Behavior / Controls

- **Graph:** live HR plot; BPM shown at top‑right inside the graph.
- **Settings (gear icon):**
  - Toggle **Press & Hold to Exit**
  - Adjust **Hold Duration** (5–15s)
  - **Forget Devices** (clears known BLE centrals / bonds) — optional utility

---

## Common Errors & Fixes

### 1) “Version bump failed” (tools/run.ps1)
- Usually because `bin/` / `obj/` contain stale artifacts.
- **Fix:** In `tools/run.ps1` choose **[4] Clean (bin/obj)**, then build again.

### 2) “No APK found under .\bin\<Config>\<TFM>”
- Build didn’t emit an APK (or wrong configuration/path).
- **Fix:** Run **[4] Clean**, then **[1] Build**. Make sure the `Framework` in `tools/run.ps1` matches your SDK (e.g., `net8.0-android` vs `net9.0-android`).

### 3) ADB not connecting
- Verify the watch and PC are on the **same Wi‑Fi**.
- If pairing is needed, use `adb pair ip:pairPort code` **before** `adb connect ip:adbPort`.
- Re‑toggle **ADB debugging** on the watch if it gets stuck.

### 4) Play Console: “This APK or bundle requires the Wear OS system feature android.hardware.type.watch…”
- You uploaded a **Wear‑only** APK to the **Phone** track.
- **Fix:** See the full **Play Console Wear OS Release Guide** below.

---

## Play Console Wear OS Release Guide

This app is **Wear OS only**. Use the Wear form factor release flow and ensure the manifest declares the watch feature.

### A) Manifest requirements (AndroidManifest.xml)

Make sure your merged manifest contains:
```xml
<manifest ...>
  <!-- Required: target only Wear OS -->
  <uses-feature android:name="android.hardware.type.watch" android:required="true" />

  <!-- Strongly recommended for BLE apps -->
  <uses-feature android:name="android.hardware.bluetooth_le" android:required="true" />
  <!-- If you read HR from sensors locally (not required for peripheral mode only) -->
  <uses-feature android:name="android.hardware.sensor.heartrate" android:required="false" />

  <!-- Optional to avoid Play thinking you require a touchscreen -->
  <uses-feature android:name="android.hardware.touchscreen" android:required="false" />
</manifest>
```

If you manage manifest via `AndroidManifest.xml` at the project root, place these `<uses-feature>` entries there. If you rely on MSBuild to inject, verify the **Merged Manifest** (Build output → `obj/.../android/AndroidManifest.xml`) contains them.

### B) Create a Wear OS listing & upload to the **Wear OS** track

In **Google Play Console**:

1. **All apps → Your app → Production → Create new release**  
2. At the top, choose **Form factor: Wear OS** (do **not** use Phone).
3. Complete the **Store listing → Wear OS** section (screenshots, icon, descriptions specific to Wear).
4. Upload your **Wear‑only APK/AAB** built from this project.
5. Resolve policy checks & roll out.

If you see the error:
> “This APK or bundle requires the Wear OS system feature android.hardware.type.watch. To publish this release on the current track, remove this artifact.”  
…it means you’re trying to upload to the **Phone** track. Switch the form factor to **Wear OS**.

### C) Optional: exclude phones entirely
The `<uses-feature android.hardware.type.watch android:required="true" />` already restricts installs to Wear. No extra `<supports-screens>` tweaks are needed.

---

## Project Structure

```
HRPeripheral/
  ├─ Views/
  │   └─ HrGraphView.cs
  ├─ HeartRateService.cs
  ├─ BlePeripheral.cs
  ├─ MainActivity.cs
  ├─ Resources/
  │   └─ layout/
  │       └─ activity_main.xml
  └─ tools/
      ├─ run.ps1
      └─ bump-version.ps1
```

---

## Script Tips (`tools/run.ps1`)

- To change target framework:
  - Run with `-Framework net8.0-android` (etc.), or edit default in the script param.
- If you use **Release Candidate**, the script appends `-rc` to `ApplicationDisplayVersion` for you.
- When deploying, the script will:
  1. Uninstall the old app (ignores failure if not found)
  2. Install the new APK
  3. Launch the app
  4. Stream **logcat** filtered to the app’s PID

---

## License
MIT
