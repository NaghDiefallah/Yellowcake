# Yellowcake Mod Manager

Yellowcake is a high-performance, streamlined mod manager designed specifically for **Nuclear Option**. Built with **Avalonia UI** and **.NET 10**, it provides an automated solution for installing, managing, and toggling plugins, ~~liveries~~, and voice packs with minimal friction.

---

## 🚀 Key Features

* **One-Click Installation**: Automated extraction and categorization of mods directly from GitHub releases or Gist manifests.
* **Intelligent Categorization**: Automatically detects and handles different mod types:
    * **Plugins**: Integrated via BepInEx junctions.
    * ~~**Liveries**: Installed directly into `StreamingAssets`.~~
    * **Voice Packs**: Managed via **WSOYappinator** integration.
* **Dependency Management**: Automatically resolves and installs required dependencies for complex mods.
* **NTFS Junctions**: Uses symbolic linking to keep your game directory clean while maintaining a central mod repository.
* **Portable & Fast**: Compiled as a single-file executable with native SQLite support for lightning-fast database operations.

---

## 🛠 Installation & Setup

1. **Download**: Grab the latest `Yellowcake.exe` from the [Releases](https://github.com/KopterBuzz/Yellowcake/releases) page.
2. **Select Game Path**: On first launch, point Yellowcake to your `NuclearOption.exe`.
3. **BepInEx Check**: If BepInEx is missing, Yellowcake will offer to install the framework for you automatically.
4. **Start Modding**: Browse the remote manifest and hit install.

---

## 📦 Project Structure

* **Yellowcake.Services**: Core logic for downloads, GitHub API interaction (`Octokit`), and mod installation.
* **Yellowcake.Models**: Data structures for Mods, Manifests, and Settings.
* **Yellowcake.Helpers**: Native utilities for NTFS Junction management and Zip extraction.
* **Yellowcake.Database**: SQLite-backed persistent storage for your local mod library.

---

## 🤝 Contributing

1. Fork the Project.
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`).
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the Branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request.

---

## 📄 License

Distributed under the **MIT License**. See `LICENSE` for more information.

**Disclaimer:** Yellowcake is an independent tool and is not affiliated with Shockfront Studios. Use at your own risk.