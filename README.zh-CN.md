# TaskScheduler

基于 **Avalonia UI** 和 **Quartz.NET** 构建的现代化桌面任务调度应用程序，提供跨平台的定时任务图形化管理界面。

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-8B5CFE)
![Quartz.NET](https://img.shields.io/badge/Quartz.NET-3.18-2E8B57)
![License](https://img.shields.io/badge/License-Apache%202.0-blue)

---

## 功能特性

- **任务管理** — 创建、编辑、删除和查看定时任务，提供丰富的图形界面
- **灵活调度** — 支持 Cron 表达式和基于时间间隔的简单触发器
- **命令执行** — 支持 Cmd、PowerShell、Python、Shell (bash) 和 Node.js 命令运行
- **执行日志** — 详细的执行历史记录，包含状态、耗时、输出和错误信息
- **仪表盘** — 概览统计（总任务数、运行中、已暂停、失败）和即将执行的任务
- **导入/导出** — 通过 JSON（剪贴板或文件）共享任务，支持版本化格式
- **工具配置** — 管理各命令类型的解释器版本和默认设置
- **系统托盘** — 最小化至系统托盘，提供快速访问菜单
- **开机自启** — 支持 Windows（注册表）、macOS（LaunchAgent）和 Linux（XDG autostart）
- **主题支持** — 浅色、深色和跟随系统主题模式，搭载中文本地化 Semi 主题

---

## 架构设计

解决方案遵循**整洁架构**，包含三个项目：

```
TaskScheduler.slnx
├── TaskScheduler.App/       # Avalonia UI 桌面应用程序 (WinExe)
│   ├── ViewModels/          # MVVM 视图模型 (CommunityToolkit.Mvvm)
│   ├── Views/               # Avalonia XAML 视图
│   ├── Dialogs/             # 任务创建/编辑对话框
│   ├── Controls/            # 自定义控件 (StatCard, StatusBadge 等)
│   ├── Services/            # 导航、命令执行、托盘图标、自启服务
│   └── Jobs/                # Quartz.NET 任务实现
│
├── TaskScheduler.Core/      # 业务逻辑 & Quartz.NET 集成
│   ├── Models/              # 领域模型 (TaskInfo, TriggerInfo 等)
│   └── Services/            # TaskSchedulerService, ExecutionLogService 等
│
└── TaskScheduler.Infra/     # 基础设施工具
    └── Helpers/             # PathHelper, DirectoryHelper, AssemblyHelper
```

### 技术栈

| 类别 | 库 |
|---|---|
| **UI 框架** | Avalonia 12.0, Semi.Avalonia (主题), Ursa (对话框), Material.Icons |
| **MVVM** | CommunityToolkit.Mvvm 8.4 (源代码生成器) |
| **任务调度** | Quartz.NET 3.18 (SQLite ADO JobStore) |
| **数据库** | Microsoft.Data.Sqlite 10.0 |
| **依赖注入** | Microsoft.Extensions.DependencyInjection |
| **日志** | Serilog (控制台 + 滚动文件) |
| **配置** | Microsoft.Extensions.Configuration (JSON + 环境变量) |

---

## 快速开始

### 环境要求

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 构建与运行

```bash
# 构建整个解决方案
dotnet build TaskScheduler.slnx

# 运行应用程序
dotnet run --project TaskScheduler.App
```

### 数据与日志

| 项目 | 位置 |
|---|---|
| **SQLite 数据库** | `%LOCALAPPDATA%\TaskSchedulerApp\TaskScheduler.db` (Windows) |
| | `~/.local/share/TaskSchedulerApp/` (Linux/macOS) |
| **日志文件** | `./Logs/log-{yyyyMMdd}.txt` (每日滚动，保留 365 天) |

---

## 配置说明

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

## 许可协议

[Apache 2.0](LICENSE)
