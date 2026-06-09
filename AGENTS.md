# AGENTS.md - TaskScheduler Development Guide

This document contains guidelines for agentic coding agents working in the TaskScheduler repository.

## Project Overview

TaskScheduler is a .NET 10.0 Avalonia desktop application using Quartz.NET for scheduling. Solution follows clean architecture with three projects:

- **TaskScheduler.App**: Avalonia UI desktop application (WinExe)
- **TaskScheduler.Core**: Core business logic with Quartz.NET integration
- **TaskScheduler.Infra**: Infrastructure helpers and utilities

## Build Commands

```bash
# Build entire solution
dotnet build TaskScheduler.slnx

# Build specific project
dotnet build TaskScheduler.App/TaskScheduler.App.csproj

# Run application
dotnet run --project TaskScheduler.App

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

### Naming Conventions
- Classes/Methods/Properties: PascalCase (`MainWindowViewModel`, `HandleToolBarItem`)
- Private fields: camelCase with underscore (`_fieldName`)
- Constants: PascalCase (`MaxRetryCount`)
- Local variables: camelCase (`localVariable`)

### File Organization
- Namespaces match folder structure (`TaskScheduler.Desktop.ViewModels`)
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

### Dependency Injection
- Use Microsoft.Extensions.DependencyInjection
- Register services in extension methods (`AddTaskScheduler()`)
- Use constructor injection
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
- UI display must show **local time** (converted from UTC)
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
├── TaskScheduler.App/  (Views, ViewModels, Services, Dialogs, Models)
├── TaskScheduler.Core/ (Quartz.NET integration, services)
├── TaskScheduler.Infra/ (PathHelper, DirectoryHelper, AssemblyHelper)
└── TaskScheduler.slnx
```

## Key Technologies

- **UI**: Avalonia UI with Semi.Avalonia theme
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
