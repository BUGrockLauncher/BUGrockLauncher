# BUGrock Launcher

[![Release: v1.0.0](https://img.shields.io/badge/Release-v1.0.0%20(Beta%202)-10b981?style=for-the-badge&logo=github)](https://github.com/BUGrockLauncher/BUGrockLauncher/releases)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-2b6cb0?style=for-the-badge&logo=windows)](https://github.com/BUGrockLauncher/BUGrockLauncher)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE.txt)

A fast, modular, and feature-rich **Minecraft Bedrock Edition Launcher for Windows 10 & 11 (x64)** supporting both modern **GDK (`.msixvc`)** and legacy **UWP (`.appx`)** versions with full multi-instance data isolation, an integrated Add-ons & Worlds manager, 3D skin viewer, and 1-click version switching.

---

## 📥 Download Editions (GitHub Releases)

Choose the edition that best fits your setup from the [**Releases Page**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases):

| Edition | Best For | Includes Runtimes? | Download |
| :--- | :--- | :--- | :--- |
| 🏆 **Full Standalone Edition (`Full-Standalone-win-x64.zip`)** | **Play Immediately (Recommended)** — Zero setup required. Works out of the box on any Windows 10/11 x64 PC. | ✅ **Yes** (Bundles `.NET 8.0 Desktop Runtime` + `VC++ 2015–2022 Runtimes` + all native shims) | 📦 [**Download Full Standalone**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases/latest) |
| ⚡ **Lightweight Edition (`Lite-win-x64.zip`)** | **Minimal Size (~11 MB ZIP)** — For users who already have `.NET 8.0` and `VC++ Redistributable` installed. | ❌ **No** (Requires system `.NET 8.0` & `VC++` runtimes below) | 🪶 [**Download Lite Edition**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases/latest) |

---

## ⚡ System Requirements

> [!NOTE]
> If you download the **Full Standalone Edition**, **`.NET 8.0 Desktop Runtime`** and **`Visual C++ 2015–2022`** are already bundled inside the launcher folder — you do **not** need to install them separately.

| Requirement | Needed For | Direct Official Link |
| :--- | :--- | :--- |
| **.NET 8.0 Desktop Runtime (x64)** | Required **only** for the **Lite Edition** (already bundled in Full Edition). | 📥 [**Direct Download (.exe)**](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe) |
| **Visual C++ 2015–2022 Redistributable (x64)** | Required for native C++ game shims & GDK decryption (bundled in Full Edition). | 📥 [**Direct Download (.exe)**](https://aka.ms/vs/17/release/vc_redist.x64.exe) |
| **Microsoft Edge WebView2 Runtime** | Pre-installed on Windows 10/11; used for the in-app Add-ons & Servers browser. | 📥 [**Direct Download (.exe)**](https://go.microsoft.com/fwlink/p/?LinkId=2124703) |
| **Windows Developer Mode** | **Required for UWP Versions** — Allows registering unpacked Bedrock packages. | ⚙️ `Settings ➔ System (or Privacy & Security) ➔ For developers ➔ Developer Mode: ON` |

---

## 🆕 What's New (Latest Updates & Features)

### 🗂️ Multi-Instance Architecture & Complete Data Isolation
- **Isolated Instances**: Create, rename, duplicate, upgrade, and delete independent Minecraft Bedrock instances, each with its own dedicated `com.mojang` save directory.
- **Zero Data Loss Guarantee**: Automatic pre-isolation backup system preserves existing un-isolated worlds and configurations inside `Backups instances/` before linking instance junctions.
- **World Conflict Detection**: Smart detection and resolution across both UWP (`LocalState\games\com.mojang`) and GDK (`Minecraft Bedrock\Users`) save paths.

### 🚀 High-Performance Version Manager & Smart Sorting
- **Instant Asynchronous Startup**: Background catalog fetching and multi-threaded JSON parsing for 600+ UWP and GDK versions with **zero UI freeze**.
- **Pinned Installed Versions**: Automatically sorts and pins all installed instances and external Microsoft Store versions to the very top of the `All`, `Release`, `Preview`, and `Beta` tabs (ordered from newest to oldest).
- **Quick Play Switcher**: Instantly switch between installed Release, Preview, Beta, and Imported packages directly from the **Play** tab.

### 🧩 Integrated Add-ons, Worlds & Servers Hub
- **One-Click Sideloading**: Install `.mcpack`, `.mcaddon`, `.mcworld`, `.mctemplate`, and `.zip` archives directly into any selected Minecraft instance or version.
- **In-App CurseForge & MCPEDL Browser**: Browse and download Bedrock mods, resource packs, behavior packs, and worlds directly inside the launcher with automatic download interception and installation.
- **Live Bedrock Servers Hub**: Browse popular Minecraft Bedrock servers and launch directly into a server with one click via `minecraft://` protocol integration.

### 🎨 3D Interactive Skin Viewer & Custom Skin Pack Injection
- **Live 3D Preview**: Interactive 3D Steve/Alex model preview with head tracking and skin management.
- **Built-in Skin Pack Provisioning**: Automatically injects the custom **BUG Guy Family** skin pack (17 classic skins) across installed versions and user profiles.

### 🔐 GDK (`.msixvc`) & Legacy UWP (`.appx`) Engine
- **Modern GDK Support (`1.21+`)**: Full staging, extraction, manifest patching, and `GDKLaunchShim` execution for modern GDK packages.
- **Automated Decryption Key Acquisition**: Built-in background verification and acquisition of official Microsoft Store GDK decryption keys.
- **Offline Package Import**: Import local `.appx`, `.msix`, and `.msixvc` packages from disk at any time.

### 🛡️ Hardened Security & Stability
- **Sandboxed WebView2 Controls**: Strict domain whitelisting, origin verification for web messages, disabled DevTools in production views, and safe `http`/`https` external link verification.
- **Safe Archive Extraction**: Full Zip-Slip path traversal protection when extracting downloaded add-ons and world templates.

---

## 🚀 Quick Start Guide

1. **Download**: Grab the **Full Standalone Edition** (or **Lite Edition**) `.zip` from [**Releases**](https://github.com/BUGrockLauncher/BUGrockLauncher/releases).
2. **Enable Developer Mode**: Turn on **Developer Mode** in Windows Settings (`Settings ➔ System ➔ For developers`).
3. **Extract to a Short Path**: Extract the folder to a short path (e.g., `C:\Games\BUGrock` or `D:\BUGrock`) to avoid Windows `MAX_PATH` limits.
4. **Launch**: Double-click **`BUGrock_Launcher.exe`**.
5. **Install & Play**:
   - Open the **Installations** tab to download any Release, Preview, or Beta version (or click **Import Package** to load a local `.appx` / `.msixvc` file).
   - Return to the **Play** tab, select your instance, and click **PLAY**!

---

## ⚙️ Critical Windows Services (`services.msc`)

Ensure these native Windows services remain enabled (**Automatic** or **Manual**):
- **`AppXSvc` (AppX Deployment Service)** — Registers and sideloads Minecraft packages.
- **`ClipSvc` (Client License Service)** — Decrypts modern GDK packages using your Microsoft Store license.
- **`GamingServices` (Gaming Services)** — Handles modern Bedrock launch shims and protocol activations.
- **`wuauserv` (Windows Update)** — Resolves official download links from Microsoft Delivery Optimization CDNs.

> [!WARNING]
> **Custom / Stripped Windows ISOs ("Ghost" / "Lite" Editions)**: If you use a modified Windows ISO with Microsoft Store or Xbox components removed, you must reinstall the **Microsoft Store** and **Xbox App** for package registration and license decryption to work.

---

## 🔧 Quick Troubleshooting

| Issue | Cause & Solution |
| :--- | :--- |
| **Launcher closes immediately on startup (Lite Edition)** | Install the [.NET 8.0 Desktop Runtime (x64)](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe), or use the **Full Standalone Edition**. |
| **`VCRUNTIME140.dll was not found`** | Install the [Visual C++ 2015–2022 Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe), or use the **Full Standalone Edition**. |
| **`App re-register failed (0x80073CFF)`** | Enable **Developer Mode** in Windows Settings (`For developers ➔ Developer Mode: ON`). |
| **`Minecraft.Windows.exe is encrypted`** | Ensure your Microsoft Store account owns Minecraft (or Game Pass). Start downloading Minecraft in the Microsoft Store for 3 seconds so Windows registers the license key, cancel the Store download, and retry in BUGrock Launcher. |
| **Path too long / Invalid argument** | Move the launcher folder to a shorter path such as `C:\BUGrock` or `D:\BUGrock`. |
| **Two versions running simultaneously** | Windows permits only one active instance per package family at a time. Close the running game before starting another. |

---

## 🛠️ Building & Publishing Releases from Source

### Requirements
- **Visual Studio 2022** with `.NET desktop development` (.NET 8.0 SDK), `Desktop development with C++` (MSVC v143), and Windows 10/11 SDK.
- **NuGet CLI** (for C++/WinRT package restore).

### 1. Build Full Solution (Native C++ Shims + Launcher)
```powershell
nuget restore BUGrockLauncher.sln
msbuild BUGrockLauncher.sln /p:Configuration=Release /p:Platform=x64 /p:RestorePackagesConfig=true /m
```

### 2. Publish Lightweight Edition (Framework-Dependent)
```powershell
dotnet publish BUGrockLauncher/BUGrockLauncher.csproj -c Release -r win-x64 --self-contained false -o publish/Lite
```

### 3. Publish Full Standalone Edition (Self-Contained, Includes .NET 8 Runtime)
```powershell
dotnet publish BUGrockLauncher/BUGrockLauncher.csproj -c Release -r win-x64 --self-contained true -o publish/Standalone
```

---

## 📄 License & Legal Notice

**BUGrock Launcher** is free and open-source software licensed under the **GNU General Public License v3.0 (GPL-3.0)** — see the [LICENSE.txt](LICENSE.txt) file for the full license text.

### Attribution & Modification Notice (GPL-3.0 § 5)
- **Original Project**: Based on [`mc-w10-version-launcher`](https://github.com/MCMrARM/mc-w10-version-launcher) by **MCMrARM** and contributors, and the updated fork [`dktapps-pm-pl/mc-w10-version-launcher`](https://github.com/dktapps-pm-pl/mc-w10-version-launcher) by **dktapps** and contributors (licensed under GPL-3.0).
- **Modified & Modernized by [BUGrockLauncher](https://github.com/BUGrockLauncher) (2026)**: Complete UI redesign, modular multi-instance architecture, isolated save data management, Quick Play & instant version switcher, 3D skin viewer & built-in skin pack injection, Add-ons & Worlds manager, GDK (`.msixvc`) extraction & decryption key automation, and performance/stability improvements.

### Third-Party Libraries, Fonts & Data Sources
- **[TinyXML-2](https://github.com/leethomason/tinyxml2)** — Licensed under the **zlib License**.
- **[Newtonsoft.Json](https://www.newtonsoft.com/json)** — Licensed under the **MIT License**.
- **[Microsoft.Web.WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)** — Copyright © Microsoft Corporation (Redistributable SDK License).
- **[Microsoft.Windows.CppWinRT](https://github.com/microsoft/cppwinrt)** — Licensed under the **MIT License**.
- **Bundled Fonts (`Epilogue` & `Tajawal`)** — Licensed under the **SIL Open Font License 1.1 (OFL-1.1)**.
- **GDK Version Catalog** — Sourced from [`MinecraftBedrockArchiver/GdkLinks`](https://github.com/MinecraftBedrockArchiver/GdkLinks).

### Trademark Disclaimer
- **NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.** *Minecraft* is a registered trademark of Mojang Synergies AB / Microsoft Corporation.
