# BUGrock Launcher

[![Release: v1.1.10](https://img.shields.io/badge/Release-v1.1.10-10b981?style=for-the-badge&logo=github)](https://github.com/BUGrockLauncher/BUGrockLauncher/releases)
[![Website](https://img.shields.io/badge/Website-bugrocklauncher.github.io-9e1f9e?style=for-the-badge&logo=githubpages)](https://bugrocklauncher.github.io/)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-2b6cb0?style=for-the-badge&logo=windows)](https://github.com/BUGrockLauncher/BUGrockLauncher)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE.txt)

A modular **Minecraft Bedrock Edition Launcher for Windows 10 & 11 (x64)** focused on version management, multi-instance data isolation, optional world backups, and direct add-on importing across both **GDK (`.msixvc`)** and legacy **UWP (`.appx`)** packages.

---

## Downloads & Editions

Every release is packaged into two distinct distributions available on the [**Releases Page**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases):

| Distribution | Download Size | Runtimes Included | Best For |
| :--- | :--- | :--- | :--- |
| **Full Standalone Edition** (`Full-Standalone-win-x64.zip`) | ~76 MB | **Yes** (.NET 8 Runtime + VC++ 2015-2022 + Native Shims) | **Immediate Play** — Zero prerequisites required. Runs on any clean Windows 10/11 x64 system out of the box. |
| **Lite Edition** (`Lite-win-x64.zip`) | ~10 MB | **No** (Uses system runtimes) | **Minimal Storage** — Fast download for systems with `.NET 8 Desktop Runtime` already installed. |

---

## Core Features

### 1. Version Manager (GDK & UWP)
- **Version Support**: Installs and launches official Bedrock Release, Preview, and Beta versions.
- **Dual Package Formats**: Supports modern **GDK (`.msixvc`)** packages and legacy **UWP (`.appx`)** packages.
- **Import Package**: Sideload local `.appx` or `.msixvc` files via file picker or drag-and-drop.

### 2. Multi-Instance Data Isolation (`Edit Instances`)
- **Isolated Profiles**: Create, clone, rename, and manage separate game instances in the **Edit Instances** tab.
- **Data Protection**: Each instance uses its own isolated `com.mojang` folder via directory junctions so switching versions does not overwrite or corrupt existing worlds.
- **Safe Migration**: Backs up existing un-isolated directories prior to linking new instances.

### 3. World Auto-Backup
- **Pre-Launch Backup**: Optional pre-launch routine that compresses world folders into timestamped `.zip` files before starting the game.
- **Local Data Directory**: Quick shortcut in the sidebar to open the active Minecraft saves folder in Windows Explorer.

### 4. Built-in Add-ons & Servers Browser
- **Integrated Content Tabs**: Embedded WebView2 tabs for **MCPEDL** and **CurseForge** (`Add-ons & Mods`, `Texture Packs`, `Maps & Worlds`, `Skins & Packs`).
- **Format Import**: Direct import support for `.mcpack`, `.mcaddon`, `.mcworld`, `.mctemplate`, and `.zip` archives.
- **Servers List**: Built-in `minecraftpocket-servers.com` browser with 1-click `minecraft://` quick-join.

### 5. Skin Manager & Interface Settings
- **3D Skin Preview**: Preview and upload custom `.png` character skins directly from the sidebar.
- **Low-Performance Mode**: Toggle in **Settings & Tools** to disable UI animations on lower-end PCs.

### 6. System Diagnostics (`BUGrock_Check`)
- **Dependency Check & Repair**: Bundled utility to check and start required Windows deployment and gaming services (`AppXSvc`, `ClipSvc`, `GamingServices`, `wuauserv`).

---

## System Requirements

| Component | Required For | Notes |
| :--- | :--- | :--- |
| **Operating System** | All Editions | Windows 10 (Build 17763+) or Windows 11 (64-bit) |
| **Windows Developer Mode** | UWP Versions | Required by Windows to sideload `.appx` packages (`Settings > System > For developers`) |
| **.NET 8.0 Desktop Runtime (x64)** | Lite Edition Only | [Download .NET 8](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe) (Pre-bundled in Full Standalone) |
| **Visual C++ 2015–2022 (x64)** | Lite Edition Only | [Download VC++ Redist](https://aka.ms/vs/17/release/vc_redist.x64.exe) (Pre-bundled in Full Standalone) |
| **Microsoft Edge WebView2** | In-app Browsers | Pre-installed on Windows 10/11; handles mod and server browser tabs |

---

## Quick Setup Guide

1. Download **`BUGrock-Launcher-Full-Standalone-win-x64.zip`** (or **`Lite`**) from [**Releases**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases).
2. Enable **Developer Mode** in Windows (`Settings > System > For developers > Developer Mode: ON`).
3. Extract the ZIP archive to a short path (e.g., `C:\Games\BUGrock` or `D:\BUGrock`) to prevent Windows `MAX_PATH` file path truncation.
4. Run **`BUGrock_Launcher.exe`**.
5. Switch to the **Installations** tab to install your desired Bedrock version, then return to **Play** and launch the game.

---

## Building from Source

### Prerequisites
- **Visual Studio 2022** with:
  - `.NET desktop development` (.NET 8.0 SDK)
  - `Desktop development with C++` (MSVC v143)
  - Windows 10 SDK (10.0.17763.0 or newer)
- **NuGet CLI**

### Build Commands
```powershell
# 1. Restore NuGet dependencies
nuget restore BUGrockLauncher.sln

# 2. Build native C++ helper binaries and solution
msbuild BUGrockLauncher.sln /p:Configuration=Release /p:Platform=x64 /p:RestorePackagesConfig=true /m

# 3. Publish Lite Edition
dotnet publish BUGrockLauncher/BUGrockLauncher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/Lite

# 4. Publish Full Standalone Edition
dotnet publish BUGrockLauncher/BUGrockLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/Standalone
```

---

## Legal & License

- **License**: BUGrock Launcher is free and open-source software licensed under the **GNU General Public License v3.0 (GPL-3.0)**. See [LICENSE.txt](LICENSE.txt) for full terms.
- **Third-Party Libraries**: Utilizes `Newtonsoft.Json` (MIT), `Microsoft.Web.WebView2` (Redistributable SDK License), and `TinyXML-2` (zlib).
- **Disclaimer**: **NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.** *Minecraft* is a registered trademark of Mojang Synergies AB / Microsoft Corporation.
