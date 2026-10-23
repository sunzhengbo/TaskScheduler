# TaskScheduler CLI 实现计划与技术设计文档

> **核心原则**：凡是界面（UI）有的功能，在 CLI 都有对应的实现（100% Feature Parity）。

---

## 一、背景与设计目标

`TaskScheduler` 目前已完成基于 Avalonia UI 的桌面端（`TaskScheduler.App`）主体功能开发。为了满足以下场景需求，需要构建与桌面端功能完全对等的命令行工具（`TaskScheduler.Cli`）：

1. **自动化与脚本集成**：通过命令行、Shell/PowerShell 脚本或 CI/CD 流水线批量管理定时任务、触发执行、导出日志。
2. **无头环境（Headless）运行**：在无图形界面的 Windows Server、Linux 服务器或容器环境中，通过 CLI 完成任务配置并常驻运行调度引擎（`daemon` 模式）。
3. **极客与终端工作流**：在不离开终端的情况下快速完成仪表盘查看、任务增删改查、解释器环境配置及实时命令测试。

---

## 二、技术栈与第三方库选型

CLI 项目目标框架为 **.NET 10.0**，统一通过根目录 `Directory.Packages.props` 进行中央包版本管理（Central Package Management）。

| 职责领域 | 选型库 / 组件 | 版本建议 | 选型理由与用途说明 |
| :--- | :--- | :--- | :--- |
| **CLI 命令解析与路由** | `Spectre.Console.Cli` | `0.57.2` | 强类型参数绑定（`CommandSettings`）、支持多级子命令树（如 `task create`, `config engine set`）、自动生成美观的 `--help` 帮助文档、原生支持异步命令 `AsyncCommand<T>` 与依赖注入桥接。 |
| **终端富文本与交互渲染** | `Spectre.Console` | `0.57.2` | 提供丰富的终端 UI 控件：表格（`Table`）、统计面板（`Panel`/`Grid`）、状态徽章（Markup 颜色标签）、交互式表单提示（`SelectionPrompt`/`TextPrompt`/`ConfirmationPrompt`）及进度指示器（`Status`）。 |
| **核心调度引擎** | `Quartz` + `Quartz.Serialization.SystemTextJson` | `3.18.1` (复用现有) | 复用 `TaskScheduler.Core` 中的 Quartz.NET SQLite 持久化调度能力。 |
| **数据库访问** | `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` | `10.0.1` / `2.1.13` (复用现有) | 读写与桌面端完全相同的 `TaskScheduler.db` SQLite 数据库。 |
| **依赖注入与配置** | `Microsoft.Extensions.DependencyInjection`<br>`Microsoft.Extensions.Configuration.Json`<br>`Microsoft.Extensions.Configuration.EnvironmentVariables` | `10.0.1` (复用现有) | 与桌面端保持一致的 DI 容器构建与 `appsettings.json` 配置解析。 |
| **日志组件** | `Serilog` + `Serilog.Sinks.File` + `Serilog.Sinks.Console` | `4.3.0` (复用现有) | 常规命令模式下仅写入滚动日志文件（防止干扰终端表格或 `--json` 输出）；`--verbose` 或 `daemon run` 模式下开启控制台日志输出。 |
| **剪贴板支持** | 自研 `ClipboardHelper`（位于 `TaskScheduler.Infra`） | 内置 | 基于操作系统原生命令/API（Windows: `clip` / PowerShell `Get-Clipboard` 或 Win32 API；macOS: `pbcopy`/`pbpaste`；Linux: `xclip`/`wl-clipboard`）实现零 GUI 依赖的跨平台剪贴板读写，支撑 `--clipboard` 导入导出功能。 |

---

## 三、架构调整与公共层下沉设计

### 3.1 解决方案结构（GUI 与 CLI 同目录共生架构）

```text
TaskScheduler.slnx
├── TaskScheduler.App/          # 桌面端 GUI (Avalonia UI, OutputType=WinExe，给人使用，双击无黑框)
│   ├── Views/ & ViewModels/    # 桌面端视图与视图模型
│   ├── appsettings.json        # 全局唯一配置文件（与 tscli 共享）
│   └── Program.cs              # 纯净 GUI 启动入口（单实例 Mutex + IPC 实时刷新监听）
├── TaskScheduler.Cli/          # 命令行端 CLI (Spectre.Console.Cli, OutputType=Exe, AssemblyName=tscli，给 AI 与脚本使用)
│   ├── Commands/               # 按模块划分的 CLI 命令实现 (Dashboard, Task, Log, Tool, Config, Daemon, About)
│   ├── Formatting/             # 终端表格、状态颜色、JSON 格式化输出器
│   ├── Infrastructure/         # Spectre.Console.Cli 的 DI 适配器 (TypeRegistrar / TypeResolver)
│   └── Program.cs              # 纯净 CLI 入口（编译/发布时自动合并到 TaskScheduler.App 输出目录，共用 appsettings.json）
├── TaskScheduler.Core/         # 核心业务逻辑层（GUI 与 CLI 共享）
│   ├── Jobs/                   # 下沉：CommandJob.cs
│   ├── Models/                 # 下沉：CommandModel.cs, TaskExportModel.cs, EngineSettings.cs, AppearanceSettings.cs, SyncMessage.cs
│   └── Services/               # 下沉：ICommandExecutor, CommandExecutor, SchedulerBootstrapService, IIpcSyncService, IpcSyncService
└── TaskScheduler.Infra/        # 基础设施与跨平台辅助工具
    └── Helpers/                # PathHelper, DirectoryHelper, AssemblyHelper, StartupHelper, ClipboardHelper
```

### 3.2 核心逻辑下沉清单（消除 App 私有依赖）

目前位于 `TaskScheduler.App` 中的以下非 UI 类将下沉至 `TaskScheduler.Core` 或 `TaskScheduler.Infra`，以供 `App` 和 `Cli` 共同调用：

1. **`CommandModel` 与 `CommandTypes`**：
   - 移除对 `CommunityToolkit.Mvvm`（UI 绑定通知）在 Core 层的强耦合，或在 `Core` 中提供纯 POCO/属性通知兼容模型，供 `CommandJob`、`TaskExportModel` 和 CLI 直接使用。
2. **`ICommandExecutor` 与 `CommandExecutor`**：
   - 下沉至 `TaskScheduler.Core.Services`。负责执行 PowerShell (`pwsh.exe`)、Python、Node.js、Bash、Cmd、VBScript 并捕获 `ExitCode`、`Output`、`Error`。
   - 同时将 `TaskEditorViewModel` 中的解释器自动探测逻辑（`DetectBuiltInInterpreterPath` 及 `MapCommandTypeToToolType`）提取为公共方法。
3. **`CommandJob`**：
   - 下沉至 `TaskScheduler.Core.Jobs.CommandJob`。
   - **关键兼容性处理**：由于 Quartz ADO JobStore 在 `QRTZ_JOB_DETAILS.JOB_CLASS_NAME` 列中持久化了类的程序集限定名，历史数据库中的记录可能为 `TaskScheduler.App.Jobs.CommandJob, TaskScheduler.App`。在 `ServiceProviderExtensions.InitDb()` 中增加自动迁移 SQL：
     ```sql
     UPDATE QRTZ_JOB_DETAILS
     SET JOB_CLASS_NAME = 'TaskScheduler.Core.Jobs.CommandJob, TaskScheduler.Core'
     WHERE JOB_CLASS_NAME LIKE 'TaskScheduler.App.Jobs.CommandJob%';
     ```
     确保旧任务在桌面端与 CLI 端均能无缝反序列化与执行。
4. **`TaskExportModel` 与 `CommandExportModel`**：
   - 下沉至 `TaskScheduler.Core.Models`，统一 JSON 导入导出的 Schema（Version 2）。
5. **`EngineSettings` 与 `AppearanceSettings`**：
   - 下沉至 `TaskScheduler.Core.Models`，统一 `app_settings` 表中 `engine_settings` 和 `appearance_settings` 的 JSON 序列化契约。
6. **`StartupHelper`**：
   - 下沉至 `TaskScheduler.Infra.Helpers`，支持传入目标可执行文件路径（默认指向桌面端或守护进程命令），实现在 CLI 中修改 `config engine set --startup true` 时同样能同步更新系统开机自启（Windows 注册表 / macOS LaunchAgent / Linux XDG autostart）。
7. **`SchedulerBootstrapService`（调度器启动引导服务）**：
   - 将 `App.axaml.cs` 中启动 Quartz Scheduler 的三阶段流程（记录暂停触发器 → 根据 `RescheduleOnStartup` 重算所有 `SimpleTrigger` → 恢复暂停状态 → 启动 Scheduler → 触发 `RunOnStartup` 开机任务）抽取到 `TaskScheduler.Core.Services.SchedulerBootstrapService`，供 `TaskScheduler.App` 和 `tscli daemon run` 共同复用。

---

## 四、关键技术实现细节

### 4.1 依赖注入与 `Spectre.Console.Cli` 桥接

`Spectre.Console.Cli` 通过 `ITypeRegistrar` 和 `ITypeResolver` 接口接入 `Microsoft.Extensions.DependencyInjection`：

```csharp
public sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());
    public void Register(Type service, Type implementation) => services.AddSingleton(service, implementation);
    public void RegisterInstance(Type service, object implementation) => services.AddSingleton(service, implementation);
    public void RegisterLazy(Type service, Func<object> factory) => services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) => type == null ? null : provider.GetService(type);
    public void Dispose() => (provider as IDisposable)?.Dispose();
}
```

### 4.2 数据库并发访问与 WAL 模式

当桌面端 `TaskScheduler.App` 正在后台运行时，用户可能同时打开终端执行 `tscli` 命令。为保证多进程同时读写同一个 `TaskScheduler.db` 不发生锁冲突：
- 在 `DatabaseProvider` / `InitDb` 初始化连接时启用 SQLite **WAL (Write-Ahead Logging)** 模式并设置忙等待超时：
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA synchronous = NORMAL;
  PRAGMA busy_timeout = 5000;
  ```

### 4.3 Quartz 调度器在 CLI 中的两种工作模式

1. **瞬时管理模式（Client Mode）**：
   - 适用于 `task list`、`task create`、`task update`、`task pause`、`task resume`、`task delete` 等命令。
   - CLI 进程初始化 `IScheduler` 实例但**不调用 `scheduler.Start()`**（处于 Standby 模式）。Quartz 的 `IScheduler` 在 Standby 状态下可直接对 SQLite `QRTZ_*` 表完成所有 Job 和 Trigger 的增删改查。
   - **关于 `task run <name>`（立即执行）的智能处理**：
     - 检测系统全局互斥锁 `TaskScheduler-App-{3F7E9C15-2A8D-4E1B-9C6F-8D3A5B2E7F1C}` 是否已被桌面端或 `daemon` 进程持有：
       - **若后台已有调度器运行**（且未指定 `--direct`）：调用 `ITaskSchedulerService.TriggerJobAsync(name, group)`，由后台运行中的调度器立即拾取并触发执行；若用户传入 `--wait`，CLI 轮询/监听 `execution_logs` 最新记录直至执行完成并打印输出。
       - **若后台无调度器运行**（或用户显式指定 `--direct`）：CLI 直接解析该任务的 `CommandModel`，调用 `ICommandExecutor` 在当前进程同步执行，并将执行结果通过 `IExecutionLogService.RecordExecutionAsync` 写入数据库，同时在终端实时打印退出码、标准输出与错误输出。

2. **常驻守护进程模式（Daemon Mode：`tscli daemon run`）**：
   - 尝试获取全局互斥锁 `TaskScheduler-App-{3F7E9C15-2A8D-4E1B-9C6F-8D3A5B2E7F1C}`（若桌面端已在运行，则提示冲突并安全退出，防止两个非集群 Scheduler 实例重复触发同一任务）。
   - 调用 `SchedulerBootstrapService.StartAsync()` 完成触发器重算、启动 Quartz 调度器、触发开机任务。
   - 阻塞等待 `CancellationToken`（绑定 `Console.CancelKeyPress` `Ctrl+C` 与 `AppDomain.CurrentDomain.ProcessExit`），收到退出信号时优雅调用 `scheduler.Shutdown(waitForJobsToComplete: true)`。

### 4.4 双模输出支持（人类可读表格 vs 机器可读 `--json`）

所有查询与操作命令均继承统一的全局设置基类 `GlobalCommandSettings`：
- `--json`：禁用 ANSI 颜色与表格边框，输出标准化的 JSON 结构（使用 `System.Text.Json`，CamelCase 命名），便于与 `jq`、PowerShell `ConvertFrom-Json` 或第三方程序对接。
- `--verbose`：开启详细诊断日志输出。
- 标准退出码规范：
  - `0`：执行成功
  - `1`：运行时异常或业务错误（如任务不存在、命令执行失败）
  - `2`：命令行参数校验失败（如缺少必填项、Cron 表达式格式非法）

---

## 五、UI 与 CLI 功能全量映射与命令规范

可执行文件名设为 `tscli`（或在开发期使用 `dotnet run --project TaskScheduler.Cli --`）。

### 5.1 仪表盘模块 (`dashboard`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 查看统计卡片、即将执行任务、最近活动 | `tscli dashboard` | `--upcoming-count <N>`：显示的即将执行任务数（默认 10）<br>`--recent-count <N>`：显示的最近活动数（默认 10）<br>`--json`：输出完整仪表盘 JSON 数据 |

### 5.2 任务管理模块 (`task`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 任务列表、状态筛选、关键词搜索 | `tscli task list` | `-g, --group <GROUP>`：按分组过滤<br>`-s, --state <STATE>`：按触发器状态过滤 (`Normal`, `Paused`, `Complete`, `Blocked`, `Error`, `None`)<br>`-q, --search <TEXT>`：按名称/分组/描述关键词搜索<br>`--json`：JSON 数组输出 |
| 查看任务详情、统计与最近日志 | `tscli task view <NAME>` | `-g, --group <GROUP>`：指定分组（默认 `DEFAULT`）<br>`--log-count <N>`：附带展示的最近日志条数（默认 10）<br>`--json`：输出任务详情、命令、统计与最近日志 JSON |
| 新建任务（参数化或交互式向导） | `tscli task create` | `-i, --interactive`：进入交互式终端向导逐步创建任务<br>`-n, --name <NAME>`：任务名称（必填）<br>`-g, --group <GROUP>`：任务分组（默认 `DEFAULT`，可选 `SYSTEM`/`USER`/`DAEMON` 或自定义）<br>`-d, --description <DESC>`：任务描述<br>`-p, --priority <PRIORITY>`：优先级 (`Low`, `Normal`, `High`，默认 `Normal`)<br>`-t, --trigger-type <TYPE>`：触发类型 (`Cron`, `Simple`, `OnStartup`，默认 `Cron`)<br>`--cron <EXPR>`：Cron 表达式（支持 5 字段或 6-7 字段 Quartz 格式）<br>`--interval <MINUTES>`：Simple 触发器间隔分钟数（≥1）<br>`--repeat <COUNT>`：Simple 触发器重复次数（`-1` 为无限循环）<br>`--use-boot-time`：Simple 触发器是否以系统开机时间为锚点<br>`--cmd-name <NAME>`：命令名称（默认 `命令1`）<br>`--cmd-type <TYPE>`：命令类型 (`PowerShell`, `Python`, `Node.js`, `Bash`, `Cmd`, `VBScript`)<br>`--cmd <CONTENT>`：命令脚本内容（或通过 `--cmd-file <PATH>` 从脚本文件读取）<br>`--interpreter <VERSION_OR_PATH>`：指定解释器版本名称或可执行文件路径 |
| 编辑更新任务 | `tscli task update <NAME>` | `-g, --group <GROUP>`：定位任务的分组（默认 `DEFAULT`）<br>支持与 `create` 相同的可选参数（仅更新显式传入的字段及触发器配置） |
| 立即执行任务 | `tscli task run <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`）<br>`--direct`：在当前 CLI 进程直接执行命令并记录日志<br>`--wait`：若交由后台调度器触发，等待执行完成并打印输出 |
| 暂停任务 | `tscli task pause <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`） |
| 恢复任务 | `tscli task resume <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`） |
| 切换启用/停用状态 | `tscli task toggle <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`）；启用时暂停，暂停时恢复 |
| 删除任务 | `tscli task delete <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`）<br>`-y, --yes`：跳过二次确认提示直接删除 |
| 测试运行命令（不保存任务） | `tscli task test-run` | `--type <TYPE>`：命令类型 (`PowerShell`, `Python`, `Node.js`, `Bash`, `Cmd`, `VBScript`)<br>`--cmd <CONTENT>`：要测试的命令内容（或 `--name <TASK_NAME>` 测试已有任务的命令）<br>`--interpreter <PATH>`：可选解释器路径 |
| 导出任务（文件 / 剪贴板 / 终端） | `tscli task export <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`）<br>`-o, --output <FILE>`：导出到指定 `.json` 文件<br>`-c, --clipboard`：复制到系统剪贴板（不指定 `-o` 和 `-c` 时直接输出到标准输出） |
| 导入任务（文件 / 剪贴板 / JSON 串） | `tscli task import [SOURCE]` | `<SOURCE>`：JSON 文件路径或内联 JSON 字符串<br>`-c, --clipboard`：从系统剪贴板读取 JSON 导入<br>`--overwrite`：同名同组任务存在时是否覆盖 |
| 查看单任务执行统计 | `tscli task stats <NAME>` | `-g, --group <GROUP>`：任务分组（默认 `DEFAULT`） |

### 5.3 执行日志模块 (`log`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 分页查询执行日志、状态过滤、关键词搜索 | `tscli log list` | `--page <N>`：页码（默认 `1`）<br>`--size <N>`：每页条数（默认 `50`）<br>`-s, --status <STATUS>`：状态过滤 (`Success`, `Failed`, `Running`, `Cancelled`)<br>`-j, --job <NAME>`：按任务名称过滤<br>`-g, --group <GROUP>`：按任务分组过滤<br>`-q, --search <TEXT>`：按任务名称或输出内容关键词过滤<br>`--json`：输出分页日志与总记录数 JSON |
| 查看单条日志终端输出详情 | `tscli log view <ID>` | `<ID>`：日志记录 ID<br>`--json`：输出单条日志完整 JSON |
| 复制/导出单条日志 | `tscli log export <ID>` | `-o, --output <FILE>`：保存为 `.log` / `.txt` 文件（默认生成 `<JobName>_<yyyyMMddHHmmss>.log`）<br>`-c, --clipboard`：将格式化后的日志详情复制到系统剪贴板 |
| 查看最近 N 条日志 | `tscli log recent` | `-n, --count <N>`：条数（默认 `20`） |

### 5.4 工具配置管理模块 (`tool`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 查看所有工具配置（按类型分组） | `tscli tool list` | `-t, --type <TYPE>`：按工具类型过滤 (`Python`, `PowerShell`, `Node.js`, `Bash`)<br>`--json`：JSON 输出 |
| 添加解释器工具版本 | `tscli tool add` | `-t, --type <TYPE>`：工具类型（必填：`Python`, `PowerShell`, `Node.js`, `Bash`）<br>`-v, --version <NAME>`：版本显示名称（必填，如 `Python 3.12`）<br>`-p, --path <PATH>`：可执行文件完整路径（必填）<br>`-e, --env <VARS>`：环境变量配置（可选）<br>`--default`：添加后立即设为该类型的默认版本 |
| 编辑更新工具配置 | `tscli tool update <ID>` | `<ID>`：工具记录 ID<br>`-v, --version <NAME>`：更新版本名称<br>`-p, --path <PATH>`：更新可执行文件路径<br>`-e, --env <VARS>`：更新环境变量 |
| 删除工具配置 | `tscli tool delete <ID>` | `<ID>`：工具记录 ID<br>`-y, --yes`：跳过确认直接删除 |
| 设置默认工具版本 | `tscli tool set-default <ID>` | `<ID>`：工具记录 ID，将该版本设为对应工具类型的默认版本 |

### 5.5 全局设置模块 (`config`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 查看引擎与外观设置 | `tscli config show` | `--section <engine\|appearance\|all>`（默认 `all`）<br>`--json`：JSON 输出 |
| 修改引擎设置 | `tscli config engine set` | `--retention-days <DAYS>`：日志保留天数（≥1）<br>`--max-threads <NUM>`：最大并发线程数（≥1）<br>`--startup <true\|false>`：开机自启动（自动同步系统注册表/自启项）<br>`--minimize <true\|false>`：启动时最小化到托盘<br>`--reschedule-on-startup <true\|false>`：启动时重新计算 SimpleTrigger 起始时间 |
| 修改外观设置 | `tscli config appearance set` | `--theme <System\|Light\|Dark>`：主题模式<br>`--compact <true\|false>`：紧凑模式 |
| 重置设置为默认值 | `tscli config reset` | `-y, --yes`：确认将引擎与外观设置恢复为系统默认值（30天、10线程、System主题等） |

### 5.6 关于与守护进程模块 (`about` / `daemon`)

| UI 界面功能 | CLI 命令语法 | 参数与选项说明 |
| :--- | :--- | :--- |
| 关于页面（版本、发布时间、框架、数据库位置） | `tscli about` | 显示版本号、构建时间、运行框架、平台信息、SQLite 数据库文件實際路径（支持 `--json`） |
| 后台常驻调度引擎 | `tscli daemon run` | 在终端前台或作为系统服务常驻运行 Quartz.NET 调度引擎，支持 `Ctrl+C` 优雅停机 |

---

## 六、分阶段实施路线图 (Roadmap)

### 阶段一：公共业务逻辑下沉与重构 (`TaskScheduler.Core` & `TaskScheduler.Infra`)
- [x] 在 `TaskScheduler.Infra` 中新增 `StartupHelper` 与 `ClipboardHelper`。
- [x] 将 `CommandModel`、`CommandTypes`、`TaskExportModel`、`EngineSettings`、`AppearanceSettings` 下沉至 `TaskScheduler.Core.Models`。
- [x] 将 `ICommandExecutor`、`CommandExecutor`、`SchedulerBootstrapService` 下沉至 `TaskScheduler.Core.Services`。
- [x] 将 `CommandJob` 下沉至 `TaskScheduler.Core.Jobs`，并在 `InitDb()` 中添加 `QRTZ_JOB_DETAILS.JOB_CLASS_NAME` 兼容迁移与 SQLite WAL 模式开启。
- [x] 更新 `TaskScheduler.App` 的命名空间引用，确保桌面端编译 0 错误 0 警告且功能完全正常。

### 阶段二：创建 `TaskScheduler.Cli` 项目与基础设施
- [x] 在 `Directory.Packages.props` 中引入 `Spectre.Console` 与 `Spectre.Console.Cli`。
- [x] 创建 `TaskScheduler.Cli/TaskScheduler.Cli.csproj` 并加入 `TaskScheduler.slnx`。
- [x] 实现 DI 桥接器（`TypeRegistrar` / `TypeResolver`）、全局配置加载（复用 `TaskSchedulerApp` 实例名以共享数据库）与统一输出辅助器（Table / JSON 切换）。

### 阶段三：实现仪表盘 (`dashboard`)、任务管理 (`task`) 与日志 (`log`) 命令集
- [x] 实现 `dashboard` 命令（统计概览卡片、即将执行任务列表、最近活动列表）。
- [x] 实现 `task` 全部子命令（`list`, `view`, `create`, `update`, `delete`, `run`, `pause`, `resume`, `toggle`, `test-run`, `export`, `import`, `stats`, `logs`）。
- [x] 实现 `log` 全部 4 个子命令（`list`, `view`, `recent`, `export`）。

### 阶段四：实现工具管理 (`tool`)、全局设置 (`config`)、关于 (`about`) 与守护进程 (`daemon`)
- [x] 实现 `tool` 全部 5 个子命令（`list`, `add`, `update`, `delete`, `set-default`）。
- [x] 实现 `config` 子命令（`show`, `engine`, `appearance`, `reset`）。
- [x] 实现 `about` 与 `daemon run` 常驻调度命令。

### 阶段五：编译验证与双端联调测试
- [x] 全解决方案编译检查（确保 0 警告、0 错误，Nullable 引用检查全部通过）。
- [x] 逐项运行 CLI 命令验证功能对等性（创建/编辑/导入/导出/立即执行/日志查看/工具配置/设置读写/JSON 输出）。
