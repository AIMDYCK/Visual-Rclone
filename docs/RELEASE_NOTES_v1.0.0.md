# Visual Rclone v1.0.0 — First Public Release

The first public release of **Visual Rclone**, a modern dark-themed control panel for managing Rclone network drives on Windows.

## ✨ Highlights

- **Live remote cards** — every configured remote appears as a card with real-time status, mount state, and quick actions.
- **One-click mounting** — mount any remote to a local drive letter with a single click.
- **Mount profiles** — Safe Read (anti-ransomware), High Performance, Bulk Transfer, and Custom presets.
- **Real-time traffic monitor** — live throughput chart per drive, with VFS-aware speed reporting.
- **VFS cache & SSD maintenance** — inspect and clean the VFS cache to keep your disk healthy.
- **Encrypted credentials** — your rclone configuration is protected with an encryption layer and a PIN lock.
- **System tray / background mode** — keep remotes mounted while the window is closed.
- **Full localization** — English, Spanish, German, and French.
- **Privacy-first** — no absolute paths or OS usernames are ever written to the UI or logs.

## 📦 Installation

1. Download `RcloneCommanderAdvanced.exe` from the assets below.
2. Run it — it is a **self-contained single file**, so no .NET runtime installation is required.
3. On first launch, the app will guide you through locating `rclone.exe` and setting up your remotes.

> **Requirements:** Windows 10/11 (x64). WinFsp is required for mounting and will be detected automatically.

## 🔐 Privacy & Security

- No telemetry, no analytics, no phone-home.
- Credentials are stored locally and encrypted.
- The `.gitignore` excludes `rclone.conf`, `appsettings.json`, and all secret files from the repository.

## 🙏 Credits

Designed and developed by **AIMDICK**.

Built with Visual Studio, Supermaven, Roo Code, and the DeepSeek API.

## 📄 License

MIT — see [LICENSE](https://github.com/AIMDICK/Visual-Rclone/blob/main/LICENSE) for details.
