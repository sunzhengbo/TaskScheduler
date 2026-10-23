<#
.SYNOPSIS
    在本地为当前 Git 仓库最新提交打 Tag。

.DESCRIPTION
    生成格式为 v<yyyy.MM.dd>.<shortCommit> 的本地 Tag（例如 v2026.10.23.614be1c）。
    默认创建附注标签（Annotated Tag），仅在本地创建，不推送到远程仓库。

.PARAMETER Message
    可选，Tag 的附加说明信息。默认与 Tag 名称相同。

.PARAMETER Force
    可选，若 Tag 已存在则强制覆盖 (-f)。

.PARAMETER Lightweight
    可选，创建轻量标签（Lightweight Tag）而非附注标签。

.EXAMPLE
    .\tag.ps1
    创建本地 Tag，格式如 v2026.10.23.614be1c。

.EXAMPLE
    .\tag.ps1 -Force
    若 Tag 已存在则强制覆盖。

.EXAMPLE
    .\tag.ps1 -Message "Release 2026.10.23"
    指定 Tag 说明信息。
#>

[CmdletBinding()]
param(
    [string]$Message = "",
    [switch]$Force,
    [switch]$Lightweight
)

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot

function Fail([string]$ErrorMessage, [int]$ExitCode = 1) {
    [Console]::Error.WriteLine($ErrorMessage)
    exit $ExitCode
}

# 检查 git 命令是否可用
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Fail "Git 未安装或未添加到 PATH 环境变量中。"
}

# 检查当前目录是否为有效 git 仓库
$isGitRepo = git -C $SolutionDir rev-parse --is-inside-work-tree 2>$null
if ($LASTEXITCODE -ne 0 -or $isGitRepo -ne "true") {
    Fail "'$SolutionDir' 不是有效的 Git 仓库。"
}

# 检查工作区是否有未提交的更改
$status = git -C $SolutionDir status --porcelain 2>$null
if ($status) {
    Write-Warning "工作区存在未提交的修改，Tag 将打在当前的 HEAD 提交上。"
}

# 获取最新 commit 的 short hash（先判空再执行 Trim()，防止空引用异常）
$commit = git -C $SolutionDir rev-parse --short HEAD 2>$null
if ([string]::IsNullOrWhiteSpace($commit)) {
    Fail "无法获取最新的 Git Commit Hash。"
}
$commit = $commit.Trim()

# 生成 Tag 名称: v<yyyy.MM.dd>.<commit>
$date = (Get-Date).ToString("yyyy.MM.dd")
$tagName = "v$date.$commit"

Write-Host "目标 Tag: $tagName" -ForegroundColor Cyan

# 检查 Tag 是否已经存在（使用 Git 原生引用校验）
$null = git -C $SolutionDir rev-parse --verify --quiet "refs/tags/$tagName" 2>$null
if ($LASTEXITCODE -eq 0 -and -not $Force) {
    Fail "Tag '$tagName' 已存在。如需覆盖，请指定 -Force 参数。"
}

# 组装 git tag 参数
$gitArgs = @("-C", $SolutionDir, "tag")
if ($Force) {
    $gitArgs += "-f"
}

if ($Lightweight) {
    $gitArgs += $tagName
} else {
    $tagMsg = if ([string]::IsNullOrWhiteSpace($Message)) { $tagName } else { $Message }
    $gitArgs += @("-a", $tagName, "-m", $tagMsg)
}

# 执行打 Tag（仅本地，不推送到远程）
& git @gitArgs
if ($LASTEXITCODE -ne 0) {
    Fail "创建 Tag '$tagName' 失败。" $LASTEXITCODE
}

Write-Host "Tag '$tagName' 创建成功！（仅本地打 Tag，未推送至远程仓库）" -ForegroundColor Green
