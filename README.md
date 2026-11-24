# Ohman — Overlay & UI Overhaul

<p align="center">
  <strong>Ultra-lightweight, zero-bloat hardware companion for HP OMEN and Victus laptops.</strong><br>
  Complete control over fans, thermal modes, power gain, keyboard remapping, and RGB lighting — without OMEN Gaming Hub's telemetry, ads, or background services.
</p>

<p align="center">
  <a href="https://github.com/JITHINDSK1/ohman-with-overlay-and-ui-overhaul"><img alt="GitHub Repo" src="https://img.shields.io/badge/GitHub-Repository-238636?style=for-the-badge&logo=github"></a>
  &nbsp;
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows">
  &nbsp;
  <img alt="Framework" src="https://img.shields.io/badge/.NET%20Framework-4.8%20(Native)-512BD4?style=for-the-badge">
  &nbsp;
  <img alt="License" src="https://img.shields.io/badge/License-GPL--3.0-blue?style=for-the-badge">
</p>

---

## ⚡ What is this Overhaul?

This fork transforms **Ohman** into a high-performance, everyday laptop companion with on-screen HUD displays, advanced stepped fan cooling algorithms, real-time keyboard remapping, custom per-key RGB lighting, and aggressive memory optimization:

- **Zero-RPM Fan Thresholds**: Fans completely shut off (0 RPM) when CPU & GPU temperatures are under 45°C.
- **Stepped Fan Deceleration**: Prevents harsh mechanical wear and sudden acoustic drops by ramping down speeds progressively (e.g. `4800` ➔ `3300` ➔ `2300` ➔ `1800` ➔ `0 RPM`).
- **On-Screen Display (OSD / HUD)**: Floating visual overlay with mode badges, thermal icons, and instant system state feedback.
- **Global Hotkeys**:
  - <kbd>Win</kbd> + <kbd>O</kbd>: Cycle performance modes (**Quiet**, **Balanced**, **Performance**, **Extreme / EME**) with a 5-second HUD overlay.
  - <kbd>Fn</kbd> + <kbd>Q</kbd>: Quick-toggle between active mode and max cooling **5700 RPM Turbo Mode** with HUD notification.
- **6200 RPM Hardware Overdrive**: Dedicated in-app tab for maximum fan boost.
- **System-Wide Keyboard Remapping**: Low-level Windows hook (`WH_KEYBOARD_LL`) with quick presets (Caps Lock ➔ Ctrl, Caps Lock ➔ Esc, Swap Alt ↔ Win, etc.) and interactive key-capture.
- **Per-Key RGB Lighting**: Unlocked individual key, row, and zone color customization with custom hex palettes and one-click reset.
- **Ultra-Lightweight (~20–30 MB RAM)**: Removed heavy background process polling and false-positive security scanners; continuous memory working-set trimming keeps footprint minimal.

---

## 🚀 Key Features

### 1. Thermal & Performance Modes
| Mode | Behavior & Target |
| :--- | :--- |
| **Quiet** | Fans stay completely off up to 60°C and only engage at 70°C for near-silent operation during light work or study. |
| **Balanced** | Default HP thermal curve with smart dynamic boost and zero-RPM below 45°C. |
| **Performance** | Elevated power ceilings and aggressive cooling for heavy workloads and gaming. |
| **EME (Extreme Mode)** | Unrestricted battery/AC fan control and full hardware cooling headroom. |

### 2. Fan Controls & Thermal Guard
- **Zero-RPM Mode**: True fan shutdown below 45°C across all modes.
- **Sequential Stepping**: Gradual step-by-step deceleration to ease fan motor bearing strain and avoid distracting noise spikes.
- **Turbo Profiles**:
  - `5700 RPM Mode`: Accessible anywhere via <kbd>Fn</kbd> + <kbd>Q</kbd>.
  - `6200 RPM Mode`: Dedicated in-app fan tab for extreme cooling scenarios.
- **Custom Curves**: Interactive 7-point fan curve editor with separate CPU/GPU unlink support.
- **Thermal Guard**: Emergency failsafe that restores high fan speeds if hardware temperatures exceed safety limits.

### 3. Keyboard Remapping (`WH_KEYBOARD_LL`)
- Directly intercepts keystrokes via low-level Windows keyboard hooks and maps output keys via `keybd_event`.
- Completely immune to infinite recursion loops using `LLKHF_INJECTED` filtering.
- **Popular Presets Built-in**:
  - `Caps Lock → Ctrl`
  - `Caps Lock → Escape`
  - `Swap Alt ↔ Windows`
  - `Caps Lock → Backspace`
- **Custom Mapping**: Click "Detect Key" and press any key on your keyboard, or click the desired key on the visual keyboard layout.

### 4. Custom Lighting
- Choose between **Key**, **Row**, **Zone**, or **All** granularities.
- Supports Static, Breathe, Cycle, Wave, and Windows Dynamic Lighting.
- Click any individual key on the interactive UI keyboard to assign custom hex colors.
- "Reset custom key colors" button to return to stock mode defaults in one click.

### 5. Keyboard Lock
- **Built-in Laptop Keyboard Lock**:
  - Soft-locking toggle to clean the keyboard or prevent accidental keypresses.
  - Safe low-level input filtering that keeps mice, touchpads, and recovery keys fully operational.
  - Emergency instant unlock shortcut: <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>K</kbd>.

### 6. Hotkeys & Shortcuts Summary
| Shortcut | Action |
| :--- | :--- |
| <kbd>Win</kbd> + <kbd>O</kbd> | Cycle Modes (Quiet ➔ Balanced ➔ Performance ➔ EME) + Show HUD Overlay |
| <kbd>Fn</kbd> + <kbd>Q</kbd> | Toggle 5700 RPM Max Fan Mode + Show HUD Overlay |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>K</kbd> | Emergency Keyboard Lock/Unlock |
| <kbd>Shift</kbd> + <kbd>F11</kbd> | Cycle Modes (legacy shortcut) |
| <kbd>Esc</kbd> | Minimize to system tray (when app window is focused) |

---

## 🛠️ Building & Running

### Requirements
- Windows 10 or 11 (64-bit).
- Built with the Microsoft .NET Framework 4.8 C# compiler (`csc.exe`) included with Windows — **no Visual Studio, SDK, or external dependencies required**.

### Quick Build & Run
Run from PowerShell or Command Prompt:

```cmd
:: Build preview (non-elevated, simulated hardware UI)
build.cmd preview

:: Build production binaries (requires Administrator for BIOS WMI)
build.cmd
```

Output binaries will be placed in the root (`Ohman.exe`) and `preview\Ohman.exe`.

### One-liner Build & Launch:
```powershell
cmd /c build.cmd; Start-Process -FilePath ".\Ohman.exe" -Verb RunAs
```

---

## 💻 Supported Hardware

Supports **all modern HP OMEN and Victus laptops** via HP's native BIOS WMI interface (`hpqBIntM`):
- **HP OMEN 15, 16, 17**
- **HP OMEN MAX 16**
- **HP OMEN Transcend 14 & 16**
- **HP Victus 15 & 16 (including S and R series)**

If your laptop model is not yet in the verified list, Ohman automatically detects your BIOS generation and builds a matching dynamic profile.

---

## 📁 Project Architecture

```
src/
├── Engine.cs          # Thermal policy, fan stepping logic, settings, keep-alive loop
├── Ui.cs              # Main window code, HUD overlay triggers, memory trimming, key hook
├── Ui.xaml            # Modern dark WPF layout and visual components
├── Controls.cs        # Custom segmented bars, color strip pickers, switches
├── Keyboard.cs        # Visual interactive keyboard SVG/geometry renderer
├── Hardware.cs        # WMI / BIOS mailbox communication (hpqBIntM) + Demo hardware
├── Lighting.cs        # 4-zone / per-key HID lighting controller
├── Curve.cs           # Spline-interpolated custom fan curve editor
├── Sensors.cs         # RAPL CPU package power counters & hardware sensors
└── Program.cs         # Application entry point, single-instance mutex, CLI flags
```

---

## 📄 License & Trademarks

- **Code**: Licensed under [GPL-3.0-or-later](LICENSE).
- **Fonts**: IBM Plex Sans / Mono licensed under the [SIL Open Font License 1.1](fonts/OFL.txt).
- **Trademarks**: *OMEN* and *Victus* are registered trademarks of HP Inc. This project is independent and not affiliated with HP.
