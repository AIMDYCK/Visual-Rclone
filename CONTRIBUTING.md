# Contributing to Visual Rclone

First off, thank you for considering contributing to **Visual Rclone**! 🎉

Visual Rclone is a WPF (.NET 8) control panel that lets anyone mount their cloud
drives through **rclone** without ever touching the command line. Contributions of
all kinds are welcome: bug reports, feature requests, translations, documentation
and code.

---

## 📜 Code of Conduct

Be respectful, constructive and patient. We are all here to learn and to build
something useful. Harassment, discrimination or hostile behavior will not be
tolerated.

---

## 🐛 Reporting Bugs

Before opening an issue, please:

1. **Search the [existing issues](../../issues)** to avoid duplicates.
2. Make sure you are running the **latest release**.
3. Collect the diagnostic information (see below).

Then open a new issue using the **🐛 Bug Report** template. It will ask you for:

- A clear description of the problem
- **Steps to reproduce** (numbered, precise)
- The **expected** vs **actual** behavior
- Your **Windows version**, **rclone version** and **WinFsp version**
- The **diagnostic console output** (the *View Log* button on any remote card)

> ⚠️ **Remove any personal information** (remote names, tokens, paths) from logs
> before posting them publicly.

---

## ✨ Suggesting Features

Open an issue using the **✨ Feature Request** template. Describe the problem you
are trying to solve, not only the solution you have in mind — it helps us find the
best approach.

---

## 🛠️ Development Setup

### Requirements

| Tool | Version | Notes |
|---|---|---|
| **Windows** | 10 / 11 (x64) | Required — the app uses WinFsp and WPF |
| **.NET SDK** | **8.0** or later | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Visual Studio** | 2022 (17.8+) | Workload: *.NET desktop development* |
| **or VS Code** | latest | With the *C# Dev Kit* extension |
| **rclone** | latest | [Download](https://rclone.org/downloads/) — must be in `PATH` |
| **WinFsp** | latest | [Download](https://winfsp.dev/rel/) — required to mount drives |
| **Git** | latest | [Download](https://git-scm.com/) |

### Clone and build

```cmd
git clone https://github.com/<your-user>/VisualRclone.git
cd VisualRclone
dotnet restore
dotnet build -c Debug
```

### Run

```cmd
dotnet run --project src\RcloneCommanderAdvanced
```

### Produce a self-contained single-file executable

```cmd
dotnet publish src\RcloneCommanderAdvanced -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true -p:DebugType=None
```

The output lands in:

```
src\RcloneCommanderAdvanced\bin\Release\net8.0-windows\win-x64\publish\
```

---

## 🏗️ Architecture Guidelines

Visual Rclone follows a strict **MVVM** pattern with **dependency injection**.

```
Views (XAML)  ──binding──▶  ViewModels  ──interfaces──▶  Services
```

### Rules

1. **No business logic in code-behind.** Code-behind is only for pure view
   concerns (window chrome, drag, focus, closing behavior).
2. **Services are injected** through the constructor and registered in
   [`App.xaml.cs`](src/RcloneCommanderAdvanced/App.xaml.cs). Always program
   against an **interface** in `Services/Abstractions/`.
3. **Never edit `rclone.conf` by hand.** All remote mutations must go through
   [`RcloneConfigManager`](src/RcloneCommanderAdvanced/Services/RcloneConfigManager.cs),
   which delegates to the official `rclone config` CLI.
4. **No hardcoded paths.** Use `Environment.GetFolderPath(...)` or
   `AppDomain.CurrentDomain.BaseDirectory`. Personal paths must never appear in
   the source.
5. **No hardcoded remote names.** Remotes are discovered at runtime from the
   user's `rclone.conf`.

---

## 🌍 Adding or Updating a Translation

The UI ships with four languages: `en-US`, `es-ES`, `fr-FR` and `de-DE`.

1. Copy [`Themes/Lang.en-US.xaml`](src/RcloneCommanderAdvanced/Themes/Lang.en-US.xaml)
   to `Lang.<culture>.xaml`.
2. Translate **only the values**, never the `x:Key` names.
3. Register the new culture in
   [`LocalizationService.AvailableLanguages`](src/RcloneCommanderAdvanced/Services/LocalizationService.cs).

> **Important:** when you add a new UI string, you must add the key to **all four**
> dictionaries, otherwise the fallback (English) will be shown.

---

## 🔐 Security Rules (Non-Negotiable)

- **Never commit** `rclone.conf`, `appsettings.json`, `*.key`, `*.log` or any file
  containing tokens or credentials. They are already listed in
  [`.gitignore`](.gitignore) — do not remove those rules.
- **Never hardcode** a personal path, username, remote name or secret.
- The master PIN must always be stored as a **PBKDF2-SHA256** hash with a random
  salt. Never store it in plain text.

---

## ✅ Pull Request Process

1. **Fork** the repository and create a branch from `main`:
   ```cmd
   git checkout -b feature/my-awesome-feature
   ```
2. Make your changes following the guidelines above.
3. Ensure the build is clean:
   ```cmd
   dotnet build -c Release
   ```
   It must finish with **0 warnings and 0 errors**.
4. Update the documentation (`README.md`, `CHANGELOG.md`) if your change is
   user-facing.
5. Commit with a clear message (see below) and push your branch.
6. Open a **Pull Request** against `main` and fill in the template.

### Commit message convention

We loosely follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add per-remote bandwidth limit
fix: prevent orphan rclone process on crash
docs: clarify WinFsp installation steps
i18n: add Italian translation
refactor: extract port allocation into a service
```

---

## 📂 Project Structure

```
VisualRclone/
├── .github/                    # Issue & PR templates
├── docs/                       # Screenshots and extra documentation
├── src/
│   └── RcloneCommanderAdvanced/
│       ├── Models/             # Plain data models
│       ├── Services/           # Business logic (behind interfaces)
│       ├── ViewModels/         # MVVM view models
│       ├── Views/              # XAML windows
│       ├── Converters/         # WPF value converters
│       └── Themes/             # Dark theme + language dictionaries
├── CONTRIBUTING.md
├── CHANGELOG.md
├── LICENSE
└── README.md
```

---

## 💬 Questions?

If something is unclear, open a **Discussion** or a **Feature Request** issue.
We are happy to help you get started.

Thank you for making Visual Rclone better! 🚀
