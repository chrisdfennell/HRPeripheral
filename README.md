# HR Peripheral (Wear OS / Xamarin)

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Wear%20OS-4285F4?logo=wear-os&logoColor=white)](https://wearos.google.com/)
![API](https://img.shields.io/badge/Android%20API-30–35-green)
![BLE](https://img.shields.io/badge/BLE-Peripheral-blue)
![Sensors](https://img.shields.io/badge/Sensors-Heart%20Rate-red)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#-license)

**HR Peripheral** is a compact **Wear OS** app written in **C# (.NET 9, Xamarin.Android)**.  
It continuously reads your **heart rate sensor** and broadcasts it via **Bluetooth Low Energy (BLE)** as a GATT Peripheral.  
Includes a live HR graph, smooth visuals, and a hold-to-exit gesture with countdown.

---

## Features

- Live Heart Rate Graph — smooth real-time waveform with gradient
- BLE Peripheral Mode — share HR data with connected devices
- Haptic Feedback — short tick each second, long buzz on exit
- Press-and-Hold Exit Gesture — configurable duration and enable toggle
- Persistent Settings — stored via SharedPreferences
- Battery-Friendly Dark UI — full-screen layout with subtle glow
- Foreground Service — continues running when screen is active
- No AndroidX dependencies — pure Xamarin C# code

---

## Project Structure

```
HRPeripheral/
├── HRPeripheral.csproj
├── MainActivity.cs              // UI + hold-to-exit gesture
├── HeartRateService.cs          // BLE + HR broadcast
├── Views/
│   ├── HrGraphView.cs           // Animated heart rate graph
│   └── HoldCountdownView.cs     // Circular hold-to-exit timer
├── Resources/
│   ├── layout/
│   │   ├── activity_main.xml
│   │   └── settings_activity.xml
│   ├── drawable/
│   ├── values/
│   └── xml/
└── tools/
    ├── run.ps1                  // Build & Deploy Menu Tool
    └── bump-version.ps1         // Auto-version increment
```

---

## Build Requirements

| Component        | Version / Notes           |
|------------------|---------------------------|
| .NET SDK         | 9.0 or later              |
| Android SDK      | API 30–35                 |
| Target Device    | Wear OS 3+                |
| IDE              | Visual Studio 2022        |
| PowerShell       | Recommended for tools/run.ps1 |

> No external dependencies are required.

---

## Installation & Deployment

1. Clone or extract this repository:
   ```bash
   git clone https://github.com/chrisdfennell/HRPeripheral.git
   cd HRPeripheral
   ```
2. Open `HRPeripheral.csproj` in Visual Studio 2022.
3. Select build target: `net9.0-android`
4. Connect your Wear OS watch (or emulator).
5. Run the PowerShell helper:
   ```powershell
   ./tools/run.ps1
   ```
6. Follow the on-screen menu:
   - [1] Build APK
   - [2] Deploy latest APK
   - [3] Build + Deploy
   - [4] Clean (bin/obj)
   - [5] Pair / Connect via Wi-Fi ADB

---

## Usage

| Action | Result |
|--------|---------|
| Launch app | Displays live heart rate graph |
| Press & Hold | Starts circular countdown to exit |
| Release early | Cancels countdown |
| Hold full time | Exits app with long vibration |
| Long-press Settings icon | Opens settings screen |

---

## Settings Storage

| Key | Type | Description |
|------|------|-------------|
| `hold_enabled` | bool | Enables or disables hold-to-exit |
| `hold_seconds` | int | Offset 0–10 (maps to 5–15 seconds actual) |

Formula: `seconds = 5 + offset`

---

## Troubleshooting

| Problem | Fix |
|----------|-----|
| **Version bump failed** | Clear `bin/` and `obj/` folders before running again. |
| **Version bump failed (in run.ps1)** | Run menu option [4] Clean (bin/obj), then retry. |
| **No HR shown** | Ensure Body Sensors permission is granted. |
| **App closes immediately** | Rebuild with Clean to refresh manifest and resource cache. |
| **Graph off-screen** | Adjusted layout in `activity_main.xml` (reduced width and bottom margin). |

> **Reminder:** If you see "Version bump failed" or "failed linking file resources", run option `[4] Clean (bin/obj)` before retrying.

---

## Developer Notes

- Uses `SensorManager` and `SensorType.HeartRate`
- Graph drawn with `Canvas.DrawLines()` in `HrGraphView`
- Foreground BLE service for continuous HR updates
- Press-and-hold exit gesture via `GestureDetector`
- Modular layout: only graph + top settings button visible

---

## License

MIT License  
© 2025 Christopher Fennell  
Free for personal or commercial use with attribution.

---

## Acknowledgements

Thanks to the Wear OS and .NET developer communities for keeping cross-platform development accessible.

---

*Built for experimentation, BLE testing, and Wear OS learning.*