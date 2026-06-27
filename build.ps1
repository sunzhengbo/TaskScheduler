param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "",
    [switch]$All,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot
$ProjectPath = Join-Path $SolutionDir "TaskScheduler.App\TaskScheduler.App.csproj"
$PublishBase = Join-Path $SolutionDir "publish"

function Get-CommitHash {
    $hash = git -C $SolutionDir rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Git not available, using 'local' as commit hash"
        return "local"
    }
    return $hash
}

function Get-BuildVersion {
    $now = Get-Date
    $date = $now.ToString("yyyy.MM.dd")
    $commit = Get-CommitHash
    return "$date.$commit"
}

function Update-AboutViewModel {
    param(
        [string]$Version
    )

    $viewModelPath = Join-Path $SolutionDir "TaskScheduler.App\ViewModels\AboutViewModel.cs"
    if (-not (Test-Path $viewModelPath)) {
        Write-Warning "AboutViewModel.cs not found, skipping version update"
        return
    }

    $releaseTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    $lines = Get-Content -Path $viewModelPath
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '_version = "') {
            $lines[$i] = $line -replace '_version = "[^"]*"', "_version = `"$Version`""
        }
        if ($line -match '_releaseTime = "') {
            $lines[$i] = $line -replace '_releaseTime = "[^"]*"', "_releaseTime = `"$releaseTime`""
        }
    }
    $lines | Set-Content -Path $viewModelPath

    Write-Host "  Updated AboutViewModel: Version=$Version, ReleaseTime=$releaseTime" -ForegroundColor DarkGray
}

function Invoke-Build {
    param(
        [string]$Rid
    )

    $version = Get-BuildVersion
    $outputDir = Join-Path $PublishBase $Rid

    Update-AboutViewModel -Version $version

    Write-Host ""
    Write-Host "=== Building for $Rid ===" -ForegroundColor Cyan
    Write-Host "  Version : $version"
    Write-Host "  Output  : $outputDir"
    Write-Host ""

    $publishArgs = @(
        "publish"
        $ProjectPath
        "-c", "Release"
        "-r", $Rid
        "--self-contained"
        "-p:PublishSingleFile=true"
        "-p:Version=1.0.0"
        "-p:InformationalVersion=$version"
        "-p:AssemblyVersion=1.0.0"
        "-p:FileVersion=1.0.0"
        "-o", $outputDir
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $Rid"
        exit 1
    }

    # Clean up unnecessary files
    $cleanupTargets = @(
        "*.pdb",
        "appsettings.*.json",
        "Resources"
    )
    foreach ($pattern in $cleanupTargets) {
        $items = Get-ChildItem -Path $outputDir -Filter $pattern -Recurse -ErrorAction SilentlyContinue
        foreach ($item in $items) {
            Remove-Item -Path $item.FullName -Recurse -Force
        }
    }

    Write-Host "Build succeeded: $Rid" -ForegroundColor Green

    New-Archive -Rid $Rid -Version $version -SourceDir $outputDir
}

function New-Archive {
    param(
        [string]$Rid,
        [string]$Version,
        [string]$SourceDir
    )

    $archiveName = "TaskScheduler_${Rid}_${Version}"
    $archivePath = Join-Path $PublishBase $archiveName

    Write-Host ""
    Write-Host "=== Packaging $Rid ===" -ForegroundColor Cyan

    if ($Rid -like "win-*") {
        $archivePath = "$archivePath.zip"
        Compress-Archive -Path "$SourceDir\*" -DestinationPath $archivePath -Force
    } else {
        $archivePath = "$archivePath.tar.gz"
        Push-Location $SourceDir
        & tar -czf $archivePath -C $SourceDir .
        Pop-Location
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Packaging failed for $Rid"
        exit 1
    }

    Write-Host "Package created: $archivePath" -ForegroundColor Green
}

# --- Main ---

if ($Clean -and (Test-Path $PublishBase)) {
    Write-Host "Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $PublishBase
}

if ($All) {
    $targets = @("win-x64", "linux-x64", "osx-x64")
    # Auto-detect ARM64
    if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") {
        switch ($PSVersionTable.OS) {
            { $_ -like "*Windows*" } { $targets = @("win-arm64") }
            { $_ -like "*Linux*" }   { $targets = @("linux-arm64") }
            { $_ -like "*Darwin*" }  { $targets = @("osx-arm64") }
        }
    }

    foreach ($rid in $targets) {
        Invoke-Build -Rid $rid
    }
} elseif ($Runtime -ne "") {
    Invoke-Build -Rid $Runtime
} else {
    # Auto-detect current platform
    switch ($PSVersionTable.OS) {
        { $_ -like "*Windows*" } {
            $Runtime = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "win-arm64" } else { "win-x64" }
        }
        { $_ -like "*Linux*" } {
            $Runtime = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "linux-arm64" } else { "linux-x64" }
        }
        { $_ -like "*Darwin*" } {
            $Runtime = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "osx-arm64" } else { "osx-x64" }
        }
        default {
            Write-Error "Cannot detect platform. Please specify -Runtime explicitly."
            exit 1
        }
    }
    Invoke-Build -Rid $Runtime
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
