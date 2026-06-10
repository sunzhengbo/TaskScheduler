# TaskScheduler

A modern desktop task scheduling application built with **Avalonia UI** and **Quartz.NET**, providing a cross-platform graphical interface for managing scheduled tasks.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-8B5CFE)
![Quartz.NET](https://img.shields.io/badge/Quartz.NET-3.18-2E8B57)
![License](https://img.shields.io/badge/License-Apache%202.0-blue)

---

## Features

- **Task Management** — Create, edit, delete, and view scheduled tasks with a rich UI
- **Flexible Scheduling** — Support for Cron expressions and simple interval-based triggers
- **Command Execution** — Run commands via Cmd, PowerShell, Python, Shell (bash), or Node.js
- **Execution Logging** — Detailed execution history with status, duration, output, and error tracking
- **Dashboard** — At-a-glance overview with statistics (total, running, paused, failed) and upcoming tasks
- **Task Import/Export** — Share tasks via JSON (clipboard or file) with versioned format
- **Tool Configuration** — Manage interpreter versions and defaults for each tool type
- **System Tray** — Minimize to tray with quick-access menu
- **Cross-platform Startup** — Auto-start on boot for Windows (Registry), macOS (LaunchAgent), and Linux (XDG autostart)
- **Theme Support** — Light, Dark, and System theme modes with Chinese-locale Semi theme

---

## Architecture

The solution follows **Clean Architecture** with three projects:

```
TaskScheduler.slnx
├── TaskScheduler.App/       # Avalonia UI desktop application (WinExe)
│   ├── ViewModels/          # MVVM ViewModels (CommunityToolkit.Mvvm)
│   ├── Views/               # Avalonia XAML views
│   ├── Dialogs/             # Task creation/edit dialogs
│   ├── Controls/            # Custom controls (StatCard, StatusBadge, etc.)
│   ├── Services/            # Navigation, CommandExecutor, TrayIcon, StartupHelper
│   └── Jobs/                # Quartz.NET job implementation
│
├── TaskScheduler.Core/      # Business logic & Quartz.NET integration
│   ├── Models/              # Domain models (TaskInfo, TriggerInfo, etc.)
│   └── Services/            # TaskSchedulerService, ExecutionLogService, etc.
│
└── TaskScheduler.Infra/     # Infrastructure utilities
    └── Helpers/             # PathHelper, DirectoryHelper, AssemblyHelper
```

### Technology Stack

| Category | Libraries |
|---|---|
| **UI Framework** | Avalonia 12.0, Semi.Avalonia (theme), Ursa (dialogs), Material.Icons |
| **MVVM** | CommunityToolkit.Mvvm 8.4 (source generators) |
| **Scheduling** | Quartz.NET 3.18 (SQLite ADO JobStore) |
| **Database** | Microsoft.Data.Sqlite 10.0 |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **Logging** | Serilog (console + rolling file) |
| **Configuration** | Microsoft.Extensions.Configuration (JSON + env vars) |

---

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build & Run

```bash
# Build the entire solution
dotnet build TaskScheduler.slnx

# Run the application
dotnet run --project TaskScheduler.App
```

### Data & Logs

| Item | Location |
|---|---|
| **SQLite Database** | `%LOCALAPPDATA%\TaskSchedulerApp\TaskScheduler.db` (Windows) |
| | `~/.local/share/TaskSchedulerApp/` (Linux/macOS) |
| **Log Files** | `./Logs/log-{yyyyMMdd}.txt` (rolling daily, 365-day retention) |

---

## Configuration

`TaskScheduler.App/appsettings.json`:

```json
{
  "Instance": "TaskSchedulerApp",
  "Database": {
    "connectionString": "Data Source=TaskScheduler.db"
  },
  "Serilog": {
    "MinimumLevel": { "Default": "Debug" }
  }
}
```

---

## License

[Apache 2.0](LICENSE)
