# 🫀 HR Peripheral (Wear OS / Xamarin)

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Wear%20OS-4285F4?logo=wear-os&logoColor=white)](https://wearos.google.com/)
![API](https://img.shields.io/badge/Android%20API-30–35-green)
![BLE](https://img.shields.io/badge/BLE-Peripheral-blue)
![Sensors](https://img.shields.io/badge/Sensors-Heart%20Rate-red)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#-license)

**HR Peripheral** is a modern **Wear OS** app built in **C# (.NET 9, Xamarin.Android)** that reads your **real-time heart rate** and broadcasts it over **Bluetooth Low Energy (BLE)** as a GATT Peripheral.  
It includes smooth visuals, tactile feedback, and an intuitive **press‑and‑hold exit gesture** with an optional **countdown ring**.

---

## 🚀 Highlights

- 💓 **Live Heart Rate Graph** — smooth line chart with gradients and live marker
- 🔊 **Haptic Feedback** — ticks each second during hold, long buzz on exit
- 💾 **Persistent Settings** — stores preferences in SharedPreferences
- ⚙️ **Press & Hold Exit Gesture**
  - Optional enable/disable toggle
  - Adjustable duration (5 – 15 seconds)
- 🔋 **Foreground Service** — continues tracking when screen is on
- 📶 **BLE Peripheral Mode** — share HR data with connected devices
- 🌙 **Battery‑friendly dark UI** with gradient background
- 🧩 **No AndroidX dependency**, clean native C# code

---

## 🧱 Project Layout

```plaintext
HRPeripheral/
├── HRPeripheral.csproj
├── MainActivity.cs                # Main UI + press‑and‑hold logic
├── HeartRateService.cs            # Foreground BLE + HR broadcast service
├── SettingsActivity.cs            # Minimal settings interface
├── Views/
│   ├── HrGraphView.cs             # Animated heart‑rate chart
│   └── HoldCountdownView.cs       # Circular countdown ring
├── Resources/
│   ├── layout/
│   │   ├── activity_main.xml
│   │   └── settings_activity.xml
│   ├── drawable/
│   ├── values/
│   └── xml/
└── builddeploy.ps1                # PowerShell helper to build & deploy APK
```

---

## 🧩 Build Requirements

| Component | Version / Notes |
|------------|----------------|
| **.NET SDK** | 9.0 or later |
| **Android SDK** | API 30 – 35 |
| **Target Device** | Wear OS 3+ |
| **IDE** | Visual Studio 2022 (Community or higher) |

> No external libraries are required beyond the standard Xamarin.Android packages.

---

## ⚙️ Installation & Deployment

1. **Clone or extract** this repo:
   ```bash
   git clone https://github.com/chrisdfennell/HRPeripheral.git
   cd HRPeripheral
   ```
2. **Open** `HRPeripheral.csproj` in **Visual Studio 2022**
3. **Select build target:** `net9.0-android`
4. **Connect** your Wear OS device or start an emulator
5. (Optional) Run PowerShell deploy script:
   ```powershell
   ./builddeploy.ps1
   ```
6. Grant **Body Sensors** and **Bluetooth** permissions on first launch

---

## 💡 Usage Guide

| Action | Behavior |
|--------|-----------|
| Launch App | Displays current heart rate and graph |
| Long‑press anywhere | Begins hold‑to‑exit countdown |
| Release early | Cancels exit |
| Hold full duration | Exits app (long haptic buzz) |
| Long‑press HR text | Opens Settings screen |
| Settings | Enable/disable hold‑to‑exit and set hold duration |

---

## ⚙️ Settings Storage

Settings use Android `SharedPreferences`:

| Key | Type | Description |
|------|------|-------------|
| `hold_enabled` | bool | Enables or disables hold‑to‑exit |
| `hold_seconds` | int | Offset (0–10) → actual seconds 5–15 |

> Formula: `seconds = 5 + offset`

---

## 🔧 Troubleshooting

| Problem | Likely Fix |
|----------|-------------|
| App closes on launch | Gradient ColorSpace mismatch fixed (uses `int[]` overload) |
| No HR shown | Ensure Body Sensors permission is granted |
| Countdown not visible | Check `HoldCountdownView` inflation in layout |
| Graph cut off | Adjusted `paddingBottom` in layout_main.xml |

---

## 🧠 Developer Notes

- Fully commented, educational Xamarin project
- Uses `SensorType.HeartRate` via `SensorManager`
- Foreground service with BLE broadcast
- Optional smoothing + gradient background
- Targeted for **real Wear OS devices (API 33+)**

---

## 🪪 License

MIT License  
© 2025 Christopher Fennell  
Free for personal and commercial use — attribution appreciated!

---

### ❤️ Acknowledgements

Thanks to the Wear OS developer community and the open‑source .NET team for keeping Xamarin alive for embedded projects.

---

> *“Built for learning, debugging, and experimenting with BLE + sensors on Wear OS.”*
