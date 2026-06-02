# Royal Kludge R87 Safe RGB Language Indicator

A lightweight utility (uses only ~10 MB RAM) to automatically change the RGB backlight color of your **Royal Kludge R87** keyboard (powered by the `258a` chip) based on the currently active input language (RU / EN).

## 🔥 Key Features
- **Safe Mode:** Strictly limits brightness, speed, and animation mode values at the code level. No more risk of freezing your keyboard's MCU with invalid or out-of-bounds HID packets.
- **Full RDP Support (Remote Desktop):** Unlike other tools, it intercepts "blind" layout switching (`Alt+Shift` / `Ctrl+Shift`) even inside active `mstsc.exe` sessions.
Known Limitations: > * RDP / Virtual Machines: Keyboard layout tracking is frozen while inside an active remote desktop session due to OS-level environment isolation.
- **Zero Dependencies:** No need to install heavy software (like OpenRGB), third-party drivers, or background services. It communicates directly with USB-HID.
- **Built-in Icon:** The application dynamically renders its own clean icon in the system tray upon startup, eliminating the need to bundle or carry an external `.ico` file.
- **Minimize to Tray:** Runs quietly in the background near the clock without cluttering your taskbar.

## 🚀 How to Use (For Regular Users)
1. Go to the **Releases** section on this GitHub page.
2. Download the `RgbController.exe` file.
3. Run it. The program will automatically minimize to the system tray.
4. A `config.txt` file will be created in the same folder. You can configure colors (RGB) via the graphical interface (by clicking the tray icon) or manually edit the text file.

> **💡 Tip:** To make the program launch automatically when Windows starts, press `Win + R`, type `shell:startup`, and drag a shortcut of the downloaded `.exe` file into that folder.

## 🛠 How to Compile Manually (For Developers)
You don't need Visual Studio. The code is written in pure WinForms (compatible with C# 5 / .NET 4.8), so you can compile it using the standard Windows compiler via the Command Prompt (cmd):

```bash
csc /target:winexe RgbController.cs
