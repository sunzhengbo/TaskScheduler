# TaskScheduler

基于 **Avalonia UI** 和 **Quartz.NET** 构建的现代化跨平台任务调度解决方案，提供面向用户的现代化图形桌面端（GUI）与专为 AI Agent、终端自动化设计的命令行工具（CLI）。

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-8B5CFE)
![Quartz.NET](https://img.shields.io/badge/Quartz.NET-3.18-2E8B57)
![Spectre.Console](https://img.shields.io/badge/Spectre.Console-0.54-orange)
![License](https://img.shields.io/badge/License-Apache%202.0-blue)

---

## 核心设计：双端共生 (GUI + CLI)

TaskScheduler 采用**双入口同目录共生**架构：

- **桌面端（`TaskScheduler.App`）**：采用纯净 WinExe 模式，双击打开无控制台黑框闪现，提供优雅流畅的图形化交互体验（人类优先）。
- **命令行端（`tscli`）**：采用标准 Exe 控制台模式，支持结构化输入输出与退出码，专为 AI Agent、CI/CD 运维与终端自动化设计（AI / 自动化优先）。
- **同目录共享与实时同步**：GUI 与 CLI 部署在同一根目录下，共享同一个 `appsettings.json` 与 SQLite 数据库（`TaskScheduler.db`），并通过底层命名管道（IPC）实现毫秒级双向同步——在终端通过 `tscli` 增删改查任务或触发执行时，后台调度器会自动响应并实时刷新已打开的 GUI 视图。

---

## 功能特性

- **双端功能全对等** — 凡是图形界面具备的功能，CLI 均提供完全对应的命令与参数支持。
- **灵活的任务调度** — 支持标准 5 字段 Cron、Quartz 6–7 字段表达式、时间间隔循环（SimpleTrigger，支持开机时间锚点计算）与开机自动启动任务（OnStartup）。
- **多元脚本与命令执行** — 支持 PowerShell、Python、Node.js、Shell (Bash)、Cmd 以及 Windows VBScript，支持多版本解释器环境与环境变量注入。
- **全生命周期日志与审计** — 记录每次执行的开始/结束时间、精确耗时、进程退出码、标准输出（stdout）与错误输出（stderr），提供关键词搜索与日志导出。
- **可视化仪表盘** — 任务运行统计、失败报警、即将执行倒计时列表与最近执行活动摘要。
- **无头守护进程模式（Daemon）** — 支持在无桌面环境的 Linux 服务器或后台终端以守护进程常驻运行调度引擎（`tscli daemon run`）。
- **任务导入/导出与共享** — 支持标准化 JSON 格式导入导出，并无缝集成系统剪贴板（快速复制任务与脚本）。
- **机器可读模式** — 所有 CLI 命令均支持 `--json` 输出结构化 CamelCase JSON，便于 AI Agent 及脚本链式调用。
- **跨平台系统开机自启** — 支持 Windows（注册表）、macOS（LaunchAgent）与 Linux（XDG Autostart）。
- **丰富的主题与托盘** — 浅色、深色与跟随系统主题，支持最小化至系统托盘常驻后台。

---

## 架构设计

解决方案遵循**整洁架构（Clean Architecture）**：

```
TaskScheduler.slnx
├── TaskScheduler.App/       # Avalonia UI 桌面图形客户端 (WinExe)
│   ├── ViewModels/          # MVVM 视图模型 (CommunityToolkit.Mvvm)
│   ├── Views/               # Avalonia XAML 视图界面
│   ├── Dialogs/             # 任务创建/编辑等交互弹窗
│   └── Services/            # 托盘图标、导航路由等 GUI 专属服务
│
├── TaskScheduler.Cli/       # 纯 Exe 命令行客户端 (tscli)
│   ├── Commands/            # 子命令实现 (dashboard, task, log, tool, config, daemon, about)
│   ├── Formatting/          # 终端表格、状态徽章与 JSON 统一格式化
│   └── Infrastructure/      # 严格参数拦截器、DI 容器与全局设置
│
├── TaskScheduler.Core/      # 业务核心下沉库 (GUI 与 CLI 共享)
│   ├── Jobs/                # Quartz 调度作业定义 (CommandJob)
│   ├── Models/              # 领域模型 (CommandModel, ExecutionLog, TaskExportModel 等)
│   └── Services/            # 调度核心、跨进程 IPC 同步、命令执行器、日志与配置服务
│
├── TaskScheduler.Infra/     # 基础设施工具层
│   └── Helpers/             # 路径处理、跨平台开机自启、无依赖剪贴板、目录与程序集工具
│
└── TaskScheduler.Tests/     # 自动化测试套件 (NUnit 4)
    ├── Cli/                 # CLI 格式化、拦截器与回归测试
    ├── Cron/                # Cron 表达式归一化与校验测试
    ├── Models/              # 任务与命令序列化兼容性测试
    └── Services/            # 执行器、调度器、日志服务集成测试
```

### 技术栈

| 类别 | 库 / 框架 | 说明 |
|---|---|---|
| **运行时** | .NET 10.0 (C# 13) | 现代化高性能运行时 |
| **桌面 UI** | Avalonia 12.0 + Semi.Avalonia | 跨平台桌面图形界面与现代设计风格 |
| **命令行交互** | Spectre.Console / Spectre.Console.Cli 0.54 | 终端富文本、表格渲染与严格命令行解析 |
| **任务调度** | Quartz.NET 3.18 | 企业级定时调度引擎，基于 SQLite ADO JobStore 持久化 |
| **数据存储** | Microsoft.Data.Sqlite 10.0 | SQLite 连接池、WAL 模式与事务保障 |
| **进程间通信** | System.IO.Pipes (Named Pipe) | GUI 与 CLI 间毫秒级双向事件通知与触发通信 |
| **日志框架** | Serilog 4.3 | 控制台结构化输出与滚动文件持久化 |
| **测试框架** | NUnit 4 + FluentAssertions | 单元测试与端到端自动化回归测试 |

---

## 快速开始

### 环境准备

- 安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

### 编译与构建

```bash
# 编译整个解决方案（会自动编译 Core, Infra, App, Cli 并将 tscli 输出至 App 同级目录）
dotnet build TaskScheduler.slnx

# 运行桌面图形端
dotnet run --project TaskScheduler.App

# 运行命令行工具
dotnet run --project TaskScheduler.Cli -- --help
dotnet run --project TaskScheduler.Cli -- dashboard
```

发布后，`TaskScheduler.App.exe`（GUI）与 `tscli.exe`（CLI）位于同级目录，可直接在终端使用 `./tscli` 进行调用。

---

## CLI (`tscli`) 完整用法指南

命令行程序专为**脚本化运维**与 **AI Agent 自动化**设计，支持严格的参数校验、标准退出码（`0`: 成功, `1`: 业务/执行错误, `2`: 参数错误）及 `--json` 纯净结构化输出。

### 1. 全局常用选项

- `--json`：启用机器可读输出（标准 JSON 格式，抑制终端样式字符）。
- `--verbose`：输出详细诊断日志（在 `--json` 模式下诊断日志自动流入 `stderr`，保障 `stdout` 纯净）。
- `-h|--help`：查看命令用法与参数说明。

---

### 2. 仪表盘概览 (`dashboard`)

展示调度引擎活跃状态、任务数量统计、即将执行的任务倒计时及最近活动日志：

```bash
# 人类友好终端看板
tscli dashboard

# 指定显示的待触发任务与最近日志条数
tscli dashboard --upcoming 10 --recent 15

# 输出结构化 JSON 概览
tscli dashboard --json
```

---

### 3. 任务管理 (`task`)

#### 查看任务列表与详情
```bash
# 列出所有任务
tscli task list

# 按分组、名称、状态过滤任务
tscli task list -g DEFAULT --status Normal
tscli task list --search 备份 --json

# 查看单个任务的完整配置与执行概况
tscli task view "数据库备份"
tscli task view "清理日志" -g MAINTENANCE --json
```

#### 创建任务 (`task create`)
```bash
# 交互式向导创建任务（适合人类用户）
tscli task create -i

# 创建 Cron 定时任务（支持标准 5 字段与 Quartz 6-7 字段）
tscli task create -n "数据库备份" -g DB --cron "0 0 2 * * ?" --cmd-type PowerShell --cmd "Backup-SqlDatabase -ServerInstance localhost"

# 从脚本文件创建任务，并指定执行器与描述
tscli task create -n "同步统计" --cron "*/10 * * * *" --cmd-type Python --cmd-file ./scripts/sync.py --interpreter "Python 3.12" -d "每10分钟同步业务指标"

# 创建时间间隔循环任务（SimpleTrigger，每 30 分钟执行一次，以系统开机时间为锚点）
tscli task create -n "健康检查" -t Simple --interval 30 --use-boot-time --cmd "curl -f http://localhost:8080/health"

# 创建开机自启任务（系统启动时触发执行一次）
tscli task create -n "自启初始化" -t OnStartup --cmd-type Cmd --cmd "init_env.bat"
```

#### 更新任务 (`task update`)
```bash
# 修改 Cron 触发时间
tscli task update "数据库备份" -g DB --cron "0 30 1 * * ?"

# 更新命令内容或关联脚本
tscli task update "健康检查" --cmd "curl -f https://api.example.com/health"

# 切换触发器类型并调整优先级
tscli task update "健康检查" -t Simple --interval 15 --priority High
```

#### 立即触发与测试运行 (`task run` / `task test-run`)
```bash
# 交由后台调度引擎触发执行（支持并发控制与日志记录）
tscli task run "数据库备份"

# 触发并同步等待后台执行完成，输出执行结果
tscli task run "数据库备份" --wait --timeout 60

# 强制在当前 CLI 进程内直接执行并落库记录日志
tscli task run "数据库备份" --direct

# 临时测试运行命令（不会写入执行日志数据库，方便调试脚本语法）
tscli task test-run -t Python -c "print(1 + 1)"
tscli task test-run -n "数据库备份" -g DB
tscli task test-run --cmd-file ./test.ps1 -t PowerShell
```

#### 暂停、恢复、切换与删除
```bash
# 暂停与恢复任务
tscli task pause "数据库备份"
tscli task resume "数据库备份"
tscli task toggle "数据库备份"

# 删除任务（非交互或脚本环境下需加 -y 跳过确认）
tscli task delete "数据库备份" -y
tscli task delete "临时任务" --json -y
```

#### 导入、导出与复制
```bash
# 导出任务为 JSON 文件
tscli task export "数据库备份" -o ./backup-task.json

# 将任务配置 JSON 复制到剪贴板
tscli task export "数据库备份" -c

# 导入任务（文件、标准输入或剪贴板）
tscli task import ./backup-task.json
cat ./backup-task.json | tscli task import -
tscli task import -c --overwrite

# 提取并复制任务命令脚本内容
tscli task copy-cmd "数据库备份"
tscli task copy-cmd "数据库备份" -n   # 仅输出到终端，不写入剪贴板
```

#### 统计指标与日志汇总
```bash
# 查看任务历史成功率、执行次数与耗时统计
tscli task stats "数据库备份" --json

# 查看指定任务的最近日志记录
tscli task logs "数据库备份" -n 10
```

---

### 4. 日志审计与导出 (`log`)

```bash
# 分页查询历史执行日志
tscli log list --page 1 --size 20

# 按执行状态、任务名或关键词筛选
tscli log list -s Failed -j "数据库备份"
tscli log list -q "Timeout" --json

# 查看单条日志的详细输出与错误调用栈
tscli log view 1024

# 查看最近活动执行日志
tscli log recent -n 10

# 导出格式化的日志记录至文件或系统剪贴板
tscli log export 1024 -o ./error-1024.log
tscli log export 1024 -c
```

---

### 5. 解释器工具配置 (`tool`)

管理各脚本类型（PowerShell, Python, Node.js, Bash）的可执行文件路径与默认版本：

```bash
# 查看已配置的工具列表
tscli tool list

# 添加工具环境
tscli tool add -t Python -v "Python 3.12 (Conda)" -p "C:\miniconda3\python.exe" --default
tscli tool add -t PowerShell -v "PowerShell 7" -p "pwsh.exe" -e "API_KEY=xyz;DEBUG=1"

# 更新工具配置与修改默认解释器
tscli tool update 1 -p "D:\Python312\python.exe"
tscli tool set-default 1

# 删除工具配置
tscli tool delete 2 -y
```

---

### 6. 全局设置 (`config`)

管理调度引擎参数与界面外观设置：

```bash
# 查看当前所有配置（支持 --section engine|appearance）
tscli config show
tscli config engine show
tscli config appearance show --json

# 修改调度引擎配置（日志保留天数、并发工作线程数、开机自启、重调度）
tscli config engine set --retention-days 60 --max-threads 16 --startup true --minimize true

# 修改外观设置（ThemeMode: Light/Dark/System, 紧凑模式）
tscli config appearance set --theme Dark --compact true

# 恢复默认设置
tscli config reset -y
```

---

### 7. 无头常驻守护进程 (`daemon`)

在服务器环境或无桌面交互环境下，作为持久运行的后台调度引擎：

```bash
# 前台常驻启动调度引擎（按 Ctrl+C 优雅停止并等待进行中的作业完成）
tscli daemon run

# 启动时不自动触发 OnStartup 任务
tscli daemon run --no-startup-tasks
```

---

## 数据与日志路径

| 项目 | 路径说明 |
|---|---|
| **SQLite 数据库** | `%LOCALAPPDATA%\TaskSchedulerApp\TaskScheduler.db` (Windows)<br>`~/.local/share/TaskSchedulerApp/` (Linux/macOS) |
| **GUI 日志文件** | `./Logs/log-{yyyyMMdd}.txt`（每日滚动保留） |
| **CLI 日志文件** | `./Logs/cli-log-{yyyyMMdd}.txt`（每日滚动保留） |
| **全局配置文件** | `./appsettings.json`（GUI 与 CLI 共享同级配置） |

---

## 许可协议

本项目采用 [Apache 2.0](LICENSE) 开源许可协议。
