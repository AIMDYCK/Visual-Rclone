# Changelog

All notable changes to **Visual Rclone** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - Initial Release

The first public release of **Visual Rclone** — a modern, dark-themed control
panel that removes the need for the command line when mounting cloud drives with
rclone on Windows.

### ✨ Added

#### Mounting engine
- **Dynamic remote discovery** — parses the user's `rclone.conf` at runtime.
  No remote name is ever hardcoded in the source.
- **Isolated RC ports** — every mount launches its own `rclone.exe` instance with
  a dedicated `--rc-addr` port, reserved from a configurable range and released
  on unmount. No network collisions between drives.
- **Three official mount profiles** plus a fully custom one:
  - *Safe Read* (anti-ransomware): `--read-only --vfs-cache-mode off`
  - *High Performance*: `--vfs-cache-mode full --vfs-cache-max-size 5G`
  - *Bulk Transfer*: `--vfs-cache-mode writes --buffer-size 128M`
- **Global actions** — *Mount All* and *Unmount All (Kill Tree)*.
- **Guaranteed cleanup** — on exit, every rclone process is killed and orphan
  processes from previous sessions are reaped on startup.

#### Real-time VFS monitor
- **Live transfer telemetry** per drive via the rclone RC API (`/core/stats`),
  rendered as a smooth line chart of upload/download throughput.
- **Hot bandwidth limiter** — applies a global upload/download cap to all mounted
  drives *without restarting them* (`/core/bwlimit`). Accepts presets and custom
  rates (`500K`, `1.5M`, `2G`) validated by a regex.
- **SSD maintenance** — a meter showing the size of the VFS cache
  (`%LOCALAPPDATA%\rclone\vfs` and `vfsMeta`) with a one-click *Purge SSD* action.

#### System tray & background mode
- **System tray integration** — the app can be minimized to the notification area
  and keep all drives mounted in the background.
- **Tray context menu** — *Open*, *Unmount All* and *Exit Completely*.
- **Smart close behavior** — closing the window opens a themed dialog offering
  *Run in Background*, *Exit and Unmount* or *Cancel*.

#### Configuration & security
- **Interactive Config Manager** — a modal editor for `rclone.conf` with provider
  cards and a 4-step wizard (name → provider → credentials → authenticate). It
  never edits the INI by hand: it delegates to `rclone config create/delete`, so
  secrets stay encrypted and provider quirks are handled correctly.
- **Per-remote settings** — drive letter, mount mode (network / physical),
  profile, custom arguments and enabled state, persisted per remote.
- **Master PIN** — PBKDF2-SHA256 with 210,000 iterations and a random 32-byte
  salt. The app starts locked.
- **Trust this device** — optional hardware fingerprint with an expiry
  (1 day / 7 days / 30 days / forever).
- **Delete confirmation** — removing a remote now requires an explicit
  confirmation in a themed dialog; the action can no longer be triggered by
  accident.

#### User experience
- **Dark "Server Panel" theme** — a custom WPF palette with elevated cards,
  accent highlights and a borderless window chrome.
- **Multi-language UI** — English (default/fallback), Spanish, French and German,
  switchable at runtime and persisted in the settings file.
- **Diagnostic console** — a per-drive log viewer keeping the last 100 lines of
  rclone stdout/stderr.
- **Atomic settings persistence** — `appsettings.json` is written atomically to
  avoid corruption on power loss.

### 🔐 Security
- No personal data, paths, remote names or credentials are stored in the source
  code or the repository.
- `rclone.conf`, `appsettings.json`, logs and key files are excluded from version
  control via a strict `.gitignore`.

### 📋 Requirements
- Windows 10/11 (x64)
- .NET 8 Desktop Runtime
- [rclone](https://rclone.org/downloads/)
- [WinFsp](https://winfsp.dev/rel/)

---

<!--
  Template for future releases:

## [1.1.0] - YYYY-MM-DD

### Added
- ...

### Changed
- ...

### Fixed
- ...

### Removed
- ...
-->

[1.0.0]: https://github.com/<your-user>/VisualRclone/releases/tag/v1.0.0
