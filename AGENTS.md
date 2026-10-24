# AGENTS.md - TaskScheduler Development Guide

This document contains guidelines for agentic coding agents working in the TaskScheduler repository.

## Project Overview

TaskScheduler is a .NET 10.0 desktop & CLI task scheduling solution using Quartz.NET for scheduling with SQLite persistence. Solution follows clean architecture with the following projects:

- **TaskScheduler.App**: Desktop GUI application (Avalonia UI, `OutputType=WinExe` — pure GUI for humans, zero console flash)
- **TaskScheduler.Cli**: Command-line interface application (`tscli`, Spectre.Console.Cli, `OutputType=Exe` — pure CLI for AI & scripts, automatically copied into `TaskScheduler.App`'s output/publish directory and sharing the same `appsettings.json`)
- **TaskScheduler.Core**: Core business logic with Quartz.NET integration, cross-process Named Pipe IPC sync, shared jobs, models and services
- **TaskScheduler.Infra**: Infrastructure helpers and utilities

## CLI 与 UI 功能对等与共生原则 (CLI-UI Feature Parity & Coexistence Principle)

> [!IMPORTANT]
> **凡是界面（UI）有的功能，在 CLI 都有对应的实现；且 CLI（`tscli`）与 GUI（`TaskScheduler.App`）同目录共生、共享同一个 `appsettings.json` 与同一个 SQLite 数据库，并通过 Named Pipe 实时同步状态。**
> 
> 任何开发者或 Agent 在进行功能开发、迭代或重构时，必须严格遵守双端功能对等与共生规范：
> 1. **双入口同目录共生（GUI 给人用，CLI 给 AI 用）**：`TaskScheduler.App` 采用纯净 `WinExe`（双击无黑框），`TaskScheduler.Cli`（`tscli`）采用纯净 `Exe`（标准 `stdout`/`stderr` 流与退出码，专为 AI Agent 与终端脚本设计）。编译或发布 `TaskScheduler.App` 时，会自动将 `tscli.exe` 输出到同一目录并共享同一个 `appsettings.json`。
> 2. **全功能覆盖**：UI 界面包含的全部功能（仪表盘概览统计、任务管理 CRUD、立即执行、暂停/恢复、测试运行、导入/导出/复制、执行日志分页查询与详情导出、解释器工具环境管理、引擎与系统设置、无头常驻调度等），在 CLI 中必须具备完全对应的命令与参数支持。
> 3. **同时运行与实时同步**：GUI 运行期间允许在终端随时执行 `tscli` 命令（不被单实例 Mutex 拦截），并通过 `IIpcSyncService`（Named Pipe）在 CLI 修改任务、配置、工具或触发执行时，自动唤醒后台 Quartz 调度器并实时刷新已打开的 GUI 视图与主题。
> 4. **通用逻辑下沉共享**：UI 与 CLI 共享底层的业务核心。通用逻辑（包括命令执行器 `ICommandExecutor`、作业定义 `CommandJob`、跨进程同步 `IIpcSyncService`、数据模型 `CommandModel`/`TaskExportModel`/`EngineSettings` 等）统一收敛在 `TaskScheduler.Core` 或 `TaskScheduler.Infra`。
> 5. **同步维护与演进**：当 UI 界面新增或变更功能特性时，必须同步在 CLI 中提供对应命令；反之新增 CLI 业务能力时，UI 也应具备对应交互。
> 6. **详细设计参考**：完整的 UI-CLI 功能映射矩阵、技术选型与架构细节参见 [`docs/cli-implementation-plan.md`](docs/cli-implementation-plan.md)。

## Build Commands

```bash
# Build entire solution (automatically outputs both TaskScheduler.App.exe and tscli.exe to TaskScheduler.App's bin directory)
dotnet build TaskScheduler.slnx

# Run GUI application
dotnet run --project TaskScheduler.App

# Run CLI application
dotnet run --project TaskScheduler.Cli -- --help
dotnet run --project TaskScheduler.Cli -- dashboard

# Clean and restore
dotnet clean TaskScheduler.slnx && dotnet restore TaskScheduler.slnx
```

## Testing Commands

No test projects currently exist. When added:

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test TaskScheduler.Tests/TaskScheduler.Tests.csproj

# Run specific test class
dotnet test --filter "FullyQualifiedName~TestClassName"

# Run specific test method
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## C# Conventions

- **Target Framework**: .NET 10.0
- **Nullable**: Enabled
- **Implicit Usings**: Enabled
- **Access Modifiers**: Explicit (public, private, internal)
- **Primary Constructors**: Prefer C# 12+ primary constructors for classes, structs, and dependency injection (e.g., `public class Service(IDependency dep) : IService`), eliminating boilerplate private readonly field declarations unless explicit backing fields or special assignment logic are strictly necessary.

### Naming Conventions
- Classes/Methods/Properties: PascalCase (`MainWindowViewModel`, `HandleToolBarItem`)
- Private fields: camelCase with underscore (`_fieldName`)
- Primary constructor parameters: camelCase (`public class Service(ILogger<Service> logger)`)
- Constants: PascalCase (`MaxRetryCount`)
- Local variables: camelCase (`localVariable`)

### File Organization
- Namespaces match folder structure (`TaskScheduler.Desktop.ViewModels`, `TaskScheduler.Cli.Commands`)
- File names match class names exactly
- Using order: System → Third-party → Local namespaces

### Import Guidelines
```csharp
using System;
using System.Collections.Generic;

using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Quartz;

using TaskScheduler.Core;
using TaskScheduler.Infra.Helpers;
```

### Error Handling
- Use `ArgumentException.ThrowIfNullOrWhiteSpace()` for validation
- Prefer specific exception types
- Include meaningful error messages with context

### Async Patterns
- Use `async Task` instead of `async void`
- Use `ConfigureAwait(false)` in library code
- Handle cancellation tokens when available

### Avalonia UI Patterns
- MVVM with CommunityToolkit.Mvvm
- ViewModels inherit from `ViewModelBase` (extends `ObservableValidator`)
- Use `[RelayCommand]` for commands
- Use `partial` class for ViewModels with generated commands
- Use primary constructors for dependency injection (e.g., `public partial class TaskListViewModel(ITaskSchedulerService scheduler) : ViewModelBase`)

### CLI Patterns
- Subcommand architecture (e.g., `task`, `log`, `config`, `tool`, `dashboard`, `daemon`)
- Provide both human-friendly formatted table/card output and machine-readable `--json` output
- Support interactive prompts when required parameters are omitted in interactive terminals
- Handle standard exit codes (0 = Success, 1 = General Error, 2 = Invalid Arguments, etc.)

### Dependency Injection
- Use Microsoft.Extensions.DependencyInjection
- Register services in extension methods (`AddTaskScheduler()`)
- Use primary constructors for constructor injection (e.g., `public class ExecutionLogService(DatabaseProvider db) : IExecutionLogService`)
- Prefer interfaces for abstractions

### Documentation
- XML documentation for public APIs
- Include `<summary>`, `<param>`, `<returns>` tags
- Use `<exception>` for expected exceptions

### Code Formatting
- Microsoft C# formatting conventions
- 4 spaces for indentation (no tabs)
- Opening braces on new line for methods/classes

### Time Handling Convention
- All `DateTime` values stored in the database are in **UTC**
- UI and CLI display must show **local time** (converted from UTC)
- When reading `DateTime` from SQLite, the returned value has `DateTimeKind.Unspecified`; explicitly mark it as UTC before converting:
  ```csharp
  DateTime.SpecifyKind(reader.GetDateTime(col), DateTimeKind.Utc).ToLocalTime()
  ```
- When writing to the database, always use `DateTime.UtcNow`

### Security
- Never commit secrets or connection strings
- Use configuration for sensitive data
- Validate all user inputs
- Use parameterized queries

## Project Structure

```
TaskScheduler/
├── TaskScheduler.App/   (Avalonia GUI: Views, ViewModels, Services, Dialogs, Models, appsettings.json)
├── TaskScheduler.Cli/   (tscli CLI: Commands, Formatting, Infrastructure)
├── TaskScheduler.Core/  (Quartz.NET integration, IPC sync, services, common jobs & models)
├── TaskScheduler.Infra/ (PathHelper, DirectoryHelper, AssemblyHelper, StartupHelper, ClipboardHelper)
└── TaskScheduler.slnx
```

## Key Technologies

- **UI**: Avalonia UI with Semi.Avalonia theme
- **CLI**: Spectre.Console / Spectre.Console.Cli (or System.CommandLine)
- **MVVM**: CommunityToolkit.Mvvm
- **Scheduling**: Quartz.NET with SQLite persistence
- **DI**: Microsoft.Extensions.DependencyInjection
- **Logging**: Serilog (console + file)
- **Database**: SQLite via Microsoft.Data.Sqlite

## Development Workflow

1. Build solution to verify compilation
2. Make changes following established patterns
3. Test application manually
4. Ensure nullable reference warnings are resolved
