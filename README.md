# TaskScheduler

A modern cross-platform task scheduling solution built with **Avalonia UI** and **Quartz.NET**, providing both a sleek graphical desktop application (GUI) for humans and a powerful, machine-readable command-line interface (CLI) for AI agents and automated scripts.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-8B5CFE)
![Quartz.NET](https://img.shields.io/badge/Quartz.NET-3.18-2E8B57)
![Spectre.Console](https://img.shields.io/badge/Spectre.Console-0.54-orange)
![License](https://img.shields.io/badge/License-Apache%202.0-blue)

---

## Core Design: Dual-Entry Coexistence (GUI + CLI)

TaskScheduler follows a **dual-entry co-located coexistence** architecture:

- **Desktop GUI (`TaskScheduler.App`)**: Pure `WinExe` output—no flash of a console window on launch. Provides an intuitive and responsive graphical user interface (Human-first).
- **Command-Line Interface (`tscli`)**: Pure `Exe` console binary with structured I/O and exit codes, specifically designed for AI agents, CI/CD pipelines, and DevOps workflows (AI / Script-first).
- **Shared Storage & Real-Time IPC Sync**: Both GUI and CLI reside in the same directory, sharing the same `appsettings.json` and SQLite database (`TaskScheduler.db`). When commands are executed in `tscli`, state changes are dispatched via cross-process Named Pipes to instantly notify the background Quartz scheduler and refresh the open GUI in real time.

---

## Features

- **Full Feature Parity** — Everything available in the desktop GUI has an exact counterpart in the CLI.
- **Flexible Scheduling** — Standard 5-field Cron expressions, Quartz 6-7 field format, simple interval triggers (with optional system boot-time anchoring), and startup triggers (OnStartup).
- **Multi-Script Execution** — PowerShell, Python, Node.js, Shell (Bash), Cmd, and Windows VBScript, with multi-version interpreter environment management and environment variable injection.
- **Comprehensive Execution History** — Tracks start/end timestamps, duration in milliseconds, exit codes, stdout, and stderr for every execution, with full-text search and export capabilities.
- **At-a-Glance Dashboard** — Aggregated statistics (total, running, paused, failed), countdown list of upcoming runs, and recent activity logs.
- **Headless Daemon Mode** — Run as a standalone background scheduling engine on headless Linux servers or background terminal sessions (`tscli daemon run`).
- **Task Import/Export & Clipboard** — Share tasks across machines using versioned JSON formats, with seamless system clipboard integration.
- **Machine-Readable `--json` Mode** — Every CLI command supports CamelCase JSON output, making it ideal for AI agent orchestration and tool integration.
- **Cross-Platform Auto-Start** — Boot-time auto-start support for Windows (Registry), macOS (LaunchAgent), and Linux (XDG autostart).
- **System Tray & Theming** — Light, Dark, and System theme modes, with quick-access minimization to the system tray.

---

## Architecture

The solution follows **Clean Architecture**:

```
TaskScheduler.slnx
├── TaskScheduler.App/       # Avalonia UI desktop client (WinExe)
│   ├── ViewModels/          # MVVM ViewModels (CommunityToolkit.Mvvm)
│   ├── Views/               # Avalonia XAML views
│   ├── Dialogs/             # Interactive task dialogs
│   └── Services/            # TrayIcon, navigation, and GUI-specific services
│
├── TaskScheduler.Cli/       # Pure Exe command-line client (tscli)
│   ├── Commands/            # Subcommand handlers (dashboard, task, log, tool, config, daemon, about)
│   ├── Formatting/          # Terminal tables, badges, and JSON formatting
│   └── Infrastructure/      # Strict argument interceptor, DI, and global settings
│
├── TaskScheduler.Core/      # Shared business core logic
│   ├── Jobs/                # Quartz job definitions (CommandJob)
│   ├── Models/              # Domain models (CommandModel, ExecutionLog, TaskExportModel)
│   └── Services/            # Scheduler core, IPC synchronization, command executor, logs, settings
│
├── TaskScheduler.Infra/     # Infrastructure utilities
│   └── Helpers/             # Path resolution, boot startup, clipboard, directory helpers
│
└── TaskScheduler.Tests/     # Automated test suite (NUnit 4)
    ├── Cli/                 # CLI formatting, interceptor, and regression tests
    ├── Cron/                # Cron expression normalization and validation tests
    ├── Models/              # Serialization compatibility tests
    └── Services/            # Executor, scheduler, and log service integration tests
```

### Technology Stack

| Category | Library | Purpose |
|---|---|---|
| **Runtime** | .NET 10.0 (C# 13) | High-performance modern managed runtime |
| **Desktop UI** | Avalonia 12.0 + Semi.Avalonia | Cross-platform desktop interface & design system |
| **CLI Framework** | Spectre.Console / Spectre.Console.Cli 0.54 | Rich terminal tables, prompt dialogs & strict parsing |
| **Scheduler** | Quartz.NET 3.18 | Enterprise scheduling engine with SQLite ADO JobStore |
| **Database** | Microsoft.Data.Sqlite 10.0 | Connection pooling, WAL mode, and transactional storage |
| **IPC Protocol** | System.IO.Pipes (Named Pipes) | Cross-process bidirectional synchronization and notifications |
| **Logging** | Serilog 4.3 | Rolling file persistence & structured console sink |
| **Testing** | NUnit 4 + FluentAssertions | Comprehensive unit and integration test coverage |

---

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

### Build & Run

```bash
# Build the entire solution (builds Core, Infra, App, and outputs tscli to App's directory)
dotnet build TaskScheduler.slnx

# Run desktop GUI
dotnet run --project TaskScheduler.App

# Run CLI application
dotnet run --project TaskScheduler.Cli -- --help
dotnet run --project TaskScheduler.Cli -- dashboard
```

When published, `TaskScheduler.App.exe` (GUI) and `tscli.exe` (CLI) live side-by-side in the same publish folder.

---

## CLI (`tscli`) Usage Guide

The CLI is optimized for **automation scripts** and **AI Agent integration**, featuring strict option validation, standard exit codes (`0`: Success, `1`: Error, `2`: Invalid Arguments), and pure `--json` output.

### 1. Global Options

- `--json`: Output machine-readable JSON (CamelCase, no terminal formatting codes).
- `--verbose`: Enable detailed diagnostic logs (automatically redirected to `stderr` in `--json` mode).
- `-h|--help`: Display help and command usage.

---

### 2. Dashboard (`dashboard`)

View scheduler health, task statistics, upcoming execution countdowns, and recent activities:

```bash
# Human-readable terminal view
tscli dashboard

# Customize upcoming count and recent logs limit
tscli dashboard --upcoming 10 --recent 15

# Machine-readable JSON output
tscli dashboard --json
```

---

### 3. Task Management (`task`)

#### View Tasks & Details
```bash
# List all tasks
tscli task list

# Filter by group, status, or keyword
tscli task list -g DEFAULT --status Normal
tscli task list --search "backup" --json

# View full configuration and metrics of a task
tscli task view "DatabaseBackup"
tscli task view "LogClean" -g MAINTENANCE --json
```

#### Create Tasks (`task create`)
```bash
# Interactive creation wizard
tscli task create -i

# Create a Cron task (supports 5-field and Quartz 6-7 field expressions)
tscli task create -n "DatabaseBackup" -g DB --cron "0 0 2 * * ?" --cmd-type PowerShell --cmd "Backup-SqlDatabase -ServerInstance localhost"

# Create a task from an external script file
tscli task create -n "SyncStats" --cron "*/10 * * * *" --cmd-type Python --cmd-file ./scripts/sync.py --interpreter "Python 3.12" -d "Sync business metrics every 10m"

# Create an interval-based task (SimpleTrigger: every 30m, anchored to system boot time)
tscli task create -n "HealthCheck" -t Simple --interval 30 --use-boot-time --cmd "curl -f http://localhost:8080/health"

# Create an OnStartup task (triggers once whenever the host or daemon boots)
tscli task create -n "InitEnv" -t OnStartup --cmd-type Cmd --cmd "init_env.bat"
```

#### Update Tasks (`task update`)
```bash
# Update Cron schedule
tscli task update "DatabaseBackup" -g DB --cron "0 30 1 * * ?"

# Update command script
tscli task update "HealthCheck" --cmd "curl -f https://api.example.com/health"

# Change trigger type and priority
tscli task update "HealthCheck" -t Simple --interval 15 --priority High
```

#### Trigger & Test Run (`task run` / `task test-run`)
```bash
# Trigger execution via the background scheduler engine
tscli task run "DatabaseBackup"

# Trigger and wait for completion with a timeout
tscli task run "DatabaseBackup" --wait --timeout 60

# Force direct execution within the CLI process and write execution log
tscli task run "DatabaseBackup" --direct

# Dry-run test command (no execution log written, ideal for syntax verification)
tscli task test-run -t Python -c "print(1 + 1)"
tscli task test-run -n "DatabaseBackup" -g DB
tscli task test-run --cmd-file ./test.ps1 -t PowerShell
```

#### Pause, Resume, Toggle & Delete
```bash
# Pause, resume, or toggle task state
tscli task pause "DatabaseBackup"
tscli task resume "DatabaseBackup"
tscli task toggle "DatabaseBackup"

# Delete a task (-y flag skips interactive confirmation in scripts)
tscli task delete "DatabaseBackup" -y
tscli task delete "TempTask" --json -y
```

#### Import, Export & Copy
```bash
# Export task to a JSON file
tscli task export "DatabaseBackup" -o ./backup-task.json

# Copy task configuration JSON to the system clipboard
tscli task export "DatabaseBackup" -c

# Import a task from file, stdin, or clipboard
tscli task import ./backup-task.json
cat ./backup-task.json | tscli task import -
tscli task import -c --overwrite

# Copy task script content to clipboard
tscli task copy-cmd "DatabaseBackup"
tscli task copy-cmd "DatabaseBackup" -n   # Output to console only, do not write to clipboard
```

#### Statistics & Logs
```bash
# View historical success rate, execution count, and duration statistics
tscli task stats "DatabaseBackup" --json

# View recent execution logs for a task
tscli task logs "DatabaseBackup" -n 10
```

---

### 4. Execution Logs (`log`)

```bash
# Paginated list of execution logs
tscli log list --page 1 --size 20

# Filter by execution status, task name, or search query
tscli log list -s Failed -j "DatabaseBackup"
tscli log list -q "Timeout" --json

# View detailed stdout and stderr for a specific log ID
tscli log view 1024

# View recent activity logs
tscli log recent -n 10

# Export formatted execution log to a file or clipboard
tscli log export 1024 -o ./error-1024.log
tscli log export 1024 -c
```

---

### 5. Interpreter Tools (`tool`)

Manage executable paths and default versions for runtime tools (PowerShell, Python, Node.js, Bash):

```bash
# List configured tools
tscli tool list

# Add a tool environment
tscli tool add -t Python -v "Python 3.12 (Conda)" -p "C:\miniconda3\python.exe" --default
tscli tool add -t PowerShell -v "PowerShell 7" -p "pwsh.exe" -e "API_KEY=xyz;DEBUG=1"

# Update tool path and set default version
tscli tool update 1 -p "D:\Python312\python.exe"
tscli tool set-default 1

# Delete a tool configuration
tscli tool delete 2 -y
```

---

### 6. Configuration Management (`config`)

View and modify engine and appearance settings:

```bash
# Display settings (--section engine|appearance)
tscli config show
tscli config engine show
tscli config appearance show --json

# Update engine parameters (log retention, thread concurrency, boot autostart)
tscli config engine set --retention-days 60 --max-threads 16 --startup true --minimize true

# Update appearance settings (ThemeMode: Light/Dark/System, compact mode)
tscli config appearance set --theme Dark --compact true

# Reset all configurations to system defaults
tscli config reset -y
```

---

### 7. Headless Daemon (`daemon`)

Run as a resident background scheduling engine in server or terminal environments without a GUI:

```bash
# Start scheduler daemon in foreground (press Ctrl+C for graceful shutdown)
tscli daemon run

# Start daemon without triggering OnStartup tasks
tscli daemon run --no-startup-tasks
```

---

## Data & Log Locations

| Item | Location |
|---|---|
| **SQLite Database** | `%LOCALAPPDATA%\TaskSchedulerApp\TaskScheduler.db` (Windows)<br>`~/.local/share/TaskSchedulerApp/` (Linux/macOS) |
| **GUI Logs** | `./Logs/log-{yyyyMMdd}.txt` |
| **CLI Logs** | `./Logs/cli-log-{yyyyMMdd}.txt` |
| **Configuration** | `./appsettings.json` (shared across GUI & CLI) |

---

## License

[Apache 2.0](LICENSE)
