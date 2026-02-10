# 🟡 Yellowcake Mod Manager

> **Modern free and open-source Mod Manager**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.0+-8B44AC?logo=avalonia)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)]()

A powerful, modern desktop application for managing mods in Nuclear Option. Built with .NET 10 and Avalonia UI, Yellowcake provides a seamless experience for browsing, installing, and managing game modifications.

![Yellowcake Banner](https://via.placeholder.com/1200x300/2C2F33/FFCC00?text=Yellowcake+Mod+Manager)

---

## ✨ Features

### 🎮 Core Functionality

- **One-Click Mod Installation** - Download and install mods directly from curated repositories
- **Automatic BepInEx Management** - Install, update, and uninstall BepInEx with a single click
- **Smart Dependency Resolution** - Automatically detect and resolve mod dependencies
- **Conflict Detection** - Identify and warn about incompatible mods before installation
- **Mod Enable/Disable** - Toggle mods on/off without uninstalling
- **Batch Operations** - Install, update, or remove multiple mods simultaneously

### 📦 Advanced Mod Management

- **Local Mod Installation** - Import mods from ZIP files or individual DLLs
- **Custom Manifest Support** - Switch between multiple mod repositories
- **Mod Library Sync** - Share and restore mod configurations via sync codes
- **Update Detection** - Automatic notification when mod updates are available
- **Version Control** - Track installed mod versions and update history
- **Categorization** - Organize mods by type (Plugins, Voice Packs, Liveries, Missions)

### 🚀 Performance & Analytics

- **Performance Dashboard** - Real-time download statistics and metrics
  - Total downloads and success rates
  - Average download speeds and peak performance
  - Data transfer analytics
  - Top downloaded mods leaderboard
- **Download Queue** - Parallel download management with configurable concurrency
- **Progress Tracking** - Live progress indicators for all operations
- **Performance Metrics Export** - Export analytics to CSV for analysis

### 🎨 User Experience

- **Modern UI** - Beautiful, responsive interface with smooth animations
- **Theme System** - Multiple built-in themes (Dark, Light, Synthium)
  - Custom theme support
  - Live theme switching
  - Per-user theme preferences
- **Search & Filter** - Powerful search with filtering by category, status, and tags
- **Sorting Options** - Sort by name, author, date, or popularity
- **Screenshot Viewer** - Preview mod screenshots in an integrated gallery
- **Empty States** - Helpful guidance when no content is available

### ⚡ Advanced Features

- **Keyboard Shortcuts** - Comprehensive hotkey support for power users
  - `Ctrl+F` - Focus search
  - `F5` - Refresh mod list
  - `Ctrl+,` - Open settings
  - `Ctrl+P` - Performance dashboard
  - `Ctrl+L` - Log viewer
  - `F1` - Hotkeys guide
- **Notification System** - Rich notifications with action buttons
  - Success/Error/Warning/Info messages
  - Clickable actions for quick retries
  - Toast notifications with auto-dismiss
- **Log Viewer** - Real-time application logs with filtering
  - Multiple log levels (Debug, Info, Warning, Error)
  - Search and filter capabilities
  - Export logs for troubleshooting
- **Diagnostics Window** - System health checks and troubleshooting tools
- **Auto-Update** - Automatic application updates with changelog display

### 🌐 Network & Hosting

- **Google Drive Support** - Direct downloads from Google Drive links
  - Automatic URL conversion
  - Virus scan warning bypass
  - Large file handling
- **GitHub Integration** - Fetch BepInEx versions from GitHub releases
- **Retry Logic** - Automatic retry on network failures
- **Download Resumption** - Resume interrupted downloads
- **Parallel Downloads** - Multiple simultaneous downloads with throttling

### 🔧 System Integration

- **Auto Game Detection** - Automatically locate Nuclear Option installation
- **Desktop Shortcuts** - Create modded game launcher shortcuts
- **File Associations** - Drag-and-drop mod installation
- **System Tray** - Minimize to tray for background operation
- **Command-Line Support** - Launch game with `--launch` parameter
- **Cross-Platform** - Windows, Linux, and macOS support

### 💾 Data Management

- **Local Database** - LiteDB for fast, reliable data storage
- **Import/Export** - Backup and restore mod configurations
- **Cache Management** - Intelligent caching with configurable limits
  - Thumbnail cache (50 images in memory)
  - Manifest cache (persistent between sessions)
  - Automatic cache cleanup
- **Settings Persistence** - All preferences saved locally

---

## 🖥️ System Requirements

### Minimum

- **OS**: Windows 10 (1903+), Linux (GTK3), macOS (10.15+)
- **CPU**: 64-bit processor, 1 GHz or faster
- **RAM**: 512 MB available memory
- **Storage**: 100 MB free space
- **.NET**: .NET 10 Runtime (automatically installed)

### Recommended

- **OS**: Windows 11, Ubuntu 22.04+, macOS 12+
- **RAM**: 1 GB available memory
- **Internet**: Broadband connection for mod downloads

---

## 📥 Installation

### Windows

1. Download the latest release from [Releases](https://github.com/NaghDiefallah/Yellowcake/releases)
2. Extract the ZIP archive
3. Run `Yellowcake.exe`
4. On first launch, set your Nuclear Option game path

### Linux
```
wget https://github.com/NaghDiefallah/Yellowcake/releases/latest/download/Yellowcake-linux-x64.tar.gz tar -xzf Yellowcake-linux-x64.tar.gz chmod +x Yellowcake ./Yellowcake
```
### macOS
```
curl -L https://github.com/NaghDiefallah/Yellowcake/releases/latest/download/Yellowcake-osx-x64.zip -o Yellowcake.zip unzip Yellowcake.zip chmod +x Yellowcake.app/Contents/MacOS/Yellowcake open Yellowcake.app
```

---

## 🚀 Quick Start

1. **Launch Yellowcake** - The app will auto-detect your game installation
2. **Install BepInEx** - Click "SETUP ENGINE" to install the modding framework
3. **Browse Mods** - Navigate to the Browse tab to see available mods
4. **Install a Mod** - Click on any mod and press "Install"
5. **Launch Game** - Use the "Launch Game" button to start Nuclear Option with mods

---

## 🎯 Usage Guide

### Installing Mods

**From Repository:**
1. Open the **Browse** tab
2. Search or filter for the mod you want
3. Click the mod to view details
4. Click **Install** button
5. Wait for download and installation

**From Local File:**
1. Open **Settings**
2. Click **Install from ZIP** or **Install DLL**
3. Select your mod file
4. Mod is automatically installed

### Managing Installed Mods

**Enable/Disable:**
- Click the checkbox next to any installed mod
- Disabled mods remain installed but inactive

**Update:**
- Mods with updates show a green "Update Available" badge
- Click the update button to install the latest version

**Uninstall:**
- Right-click a mod and select "Uninstall"
- Or use the trash icon in the mod details

### BepInEx Management

**Install:**
1. Open **Settings**
2. Select a BepInEx version from dropdown
3. Click **INSTALL**

**Uninstall:**
- Click "Uninstall BepInEx" in Settings
- Confirm the removal

### Performance Monitoring

**View Statistics:**
1. Press `Ctrl+P` or click the stats icon
2. View download metrics, speeds, and history
3. Export data as CSV for analysis

**Clear History:**
- Click "Clear History" to reset all performance data

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `F1` | Show hotkeys guide |
| `F5` | Refresh mod list |
| `Ctrl+F` | Focus search box |
| `Ctrl+,` | Open settings |
| `Ctrl+L` | Open log viewer |
| `Ctrl+P` | Performance dashboard |
| `Ctrl+B` | Toggle batch mode |
| `Ctrl+A` | Select all (batch mode) |
| `Ctrl+Shift+D` | Diagnostics window |
| `Escape` | Close overlays |

---

## 🎨 Themes

Yellowcake includes multiple themes:

- **Dark** (Default) - Modern dark theme
- **Light** - Clean light theme
- **Synthium** - Purple accent theme

**Custom Themes:**
1. Navigate to `%AppData%/Yellowcake/Themes` (Windows)
2. Create a new `.axaml` theme file
3. Reload themes in Settings
4. Select your custom theme

---

## 🔧 Configuration

### Settings Location

- **Windows**: `%LocalAppData%\Yellowcake\`
- **Linux**: `~/.local/share/Yellowcake/`
- **macOS**: `~/Library/Application Support/Yellowcake/`

### Configuration Files

- `data.db` - Local database (mods, settings, performance)
- `cache/` - Thumbnail and manifest cache
- `logs/` - Application logs
- `themes/` - Custom theme files

---

## 🛠️ Development

### Prerequisites

- .NET 10 SDK
- Visual Studio 2022+ or JetBrains Rider
- Git

### Building from Source
git clone https://github.com/NaghDiefallah/Yellowcake.git cd Yellowcake dotnet restore dotnet build dotnet run

### Key Dependencies

- **Avalonia UI** 11.0+ - Cross-platform UI framework
- **CommunityToolkit.Mvvm** 8.4+ - MVVM helpers
- **LiteDB** 5.0+ - Embedded database
- **Serilog** 4.0+ - Logging framework
- **Octokit** 13.0+ - GitHub API client
- **SharpCompress** 0.37+ - Archive extraction

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- **Nuclear Option** - The game this mod manager is built for
- **BepInEx Team** - For the modding framework
- **Avalonia Team** - For the amazing UI framework
- **Community Contributors** - For mod submissions and testing

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/NaghDiefallah/Yellowcake/issues)
- **Discussions**: [GitHub Discussions](https://github.com/NaghDiefallah/Yellowcake/discussions)
- **Discord**: [Join our Discord](#)

---

## 📊 Statistics

![GitHub Downloads](https://img.shields.io/github/downloads/NaghDiefallah/Yellowcake/total?style=flat-square)
![GitHub Stars](https://img.shields.io/github/stars/NaghDiefallah/Yellowcake?style=flat-square)
![GitHub Issues](https://img.shields.io/github/issues/NaghDiefallah/Yellowcake?style=flat-square)
![GitHub Last Commit](https://img.shields.io/github/last-commit/NaghDiefallah/Yellowcake?style=flat-square)

---

<div align="center">

**Made with ❤️ by NaghDiefallah**

[⬆ Back to Top](#-yellowcake-mod-manager)

</div>