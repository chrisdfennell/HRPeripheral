# 🫀 HR Peripheral (Android Wear / Xamarin)

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Wear%20OS-4285F4?logo=wear-os&logoColor=white)](https://wearos.google.com/)
![API](https://img.shields.io/badge/Android%20API-30–35-green)
![BLE](https://img.shields.io/badge/BLE-Peripheral-blue)
![Sensors](https://img.shields.io/badge/Sensors-Heart%20Rate-red)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#-license)

**HR Peripheral** is a Wear OS app written in **C# (Xamarin.Android)** that broadcasts your **real-time heart rate via BLE** and shows it on-screen.  
It also features a **press-and-hold exit gesture** and a small **settings menu** to control behavior.

---

## 📱 Features

- Realtime **Heart Rate Monitoring** via `SensorType.HeartRate`
- **BLE Peripheral Broadcasting** using GATT services
- **Foreground Service** keeps data live while screen is on
- **Press & Hold to Exit** gesture (customizable hold duration)
- **Visual countdown ring** + haptic feedback
- **Settings screen** to adjust:
  - Enable/disable hold-to-exit
  - Hold duration (5–15 seconds)
- Works fully offline (no permissions beyond BLE + sensors)

---

## 🧩 Project Structure

```
HRPeripheral/
├── HRPeripheral.csproj
├── MainActivity.cs                # Core UI + Hold-to-exit logic
├── HeartRateService.cs            # Foreground BLE + HR service
├── SettingsActivity.cs            # Custom settings UI (no AndroidX dependency)
├── Views/
│   └── HoldCountdownView.cs       # Circular progress ring
├── Resources/
│   ├── layout/
│   │   ├── activity_main.xml
│   │   └── settings_activity.xml
│   ├── drawable/
│   ├── xml/
│   └── values/
└── builddeploy.ps1                # Helper script for building APKs
```

---

## ⚙️ Build Requirements

| Component | Version |
|------------|----------|
| .NET SDK   | 9.0+     |
| Android SDK | API 30+ |
| Target     | **Wear OS 3+ (API 30–35)** |
| IDE        | Visual Studio 2022 (Community or higher) |

> NuGet packages: You likely already have **Xamarin.AndroidX.Core** / **AppCompat** via your template. No AndroidX Preference is required (custom settings view).

---

## 🛠️ Setup & Deployment

1. Clone or extract the project:
   ```bash
   git clone https://github.com/yourname/HRPeripheral.git
   cd HRPeripheral
   ```

2. Open in **Visual Studio 2022** and select:
   ```
   Configuration: Debug
   Target Framework: net9.0-android
   ```

3. Plug in your Wear OS device or emulator.

4. Optional: run the helper to build & deploy:
   ```powershell
   ./builddeploy.ps1
   ```

5. On first launch, grant permissions (Body Sensors, Bluetooth as needed).

---

## 💡 Usage

- Launch **HR Peripheral** on your watch.
- The screen displays your current heart rate.
- To **exit**, **press and hold anywhere** on the screen for the configured duration (default: 10 s).
  - A circular ring fills as you hold.
  - Small haptics tick each second; a long buzz confirms exit.
- To change behavior:
  - Long-press the HR value to open **Settings**.
  - Toggle *Press & Hold to Exit* or adjust *Hold Duration* (5–15 s).

---

## 🧱 Settings Persistence

Settings are stored in `SharedPreferences`:

| Key            | Type  | Description                                   |
|----------------|-------|-----------------------------------------------|
| `hold_enabled` | bool  | Enables/disables hold-to-exit                 |
| `hold_seconds` | int   | **Offset** (0–10) → actual seconds 5–15       |

> Mapping: `seconds = 5 + offset`

---

## 🧰 Troubleshooting

| Issue | Fix |
|------|-----|
| App closes immediately on launch | On Android 13+, dynamic receivers must be registered with flags. In this project we use `RegisterReceiver(..., ReceiverFlags.NotExported)` to satisfy the requirement. |
| No heart rate shown | Ensure the device has a HR sensor and that Body Sensors permission is granted. |
| Countdown ring not visible | Confirm `HoldCountdownView` is present in `activity_main.xml` and its visibility toggles on hold. |

---

## 🧑‍💻 Developer Notes

- C# / .NET 9 + Xamarin.Android
- No AndroidX Preference dependency
- Heart rate via `SensorType.HeartRate`
- BLE peripheral GATT service broadcast
- Wear OS 3+ tested

---

## 📜 License

MIT License  
© 2025 Christopher Fennell
