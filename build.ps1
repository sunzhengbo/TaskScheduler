param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "",
    [switch]$All,
    [switch]$Clean,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$SolutionDir = $PSScriptRoot
$ProjectPath = Join-Path $SolutionDir "TaskScheduler.App\TaskScheduler.App.csproj"
$TestProjectPath = Join-Path $SolutionDir "TaskScheduler.Tests\TaskScheduler.Tests.csproj"
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

function Update-AppMetadata {
    param(
        [string]$Version
    )

    $metadataPath = Join-Path $SolutionDir "TaskScheduler.Core\Models\AppMetadata.cs"
    if (-not (Test-Path $metadataPath)) {
        Write-Warning "AppMetadata.cs not found, skipping version update"
        return
    }

    $releaseTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    $lines = Get-Content -Path $metadataPath
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'Version = "') {
            $lines[$i] = $line -replace 'Version = "[^"]*"', "Version = `"$Version`""
        }
        if ($line -match 'ReleaseTime = "') {
            $lines[$i] = $line -replace 'ReleaseTime = "[^"]*"', "ReleaseTime = `"$releaseTime`""
        }
    }
    $lines | Set-Content -Path $metadataPath

    Write-Host "  Updated AppMetadata: Version=$Version, ReleaseTime=$releaseTime" -ForegroundColor DarkGray
}

function Invoke-Tests {
    if ($SkipTests) {
        Write-Host "Skipping tests (-SkipTests specified)" -ForegroundColor Yellow
        return
    }

    if (-not (Test-Path $TestProjectPath)) {
        Write-Warning "Test project not found at $TestProjectPath, skipping tests"
        return
    }

    Write-Host ""
    Write-Host "=== Running Unit & Regression Tests ===" -ForegroundColor Cyan
    & dotnet test $TestProjectPath -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed! Aborting build."
        exit 1
    }
    Write-Host "Tests passed successfully." -ForegroundColor Green
}

function Invoke-Build {
    param(
        [string]$Rid
    )

    $version = Get-BuildVersion
    $outputDir = Join-Path $PublishBase $Rid

    Update-AppMetadata -Version $version

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

    # Ensure README.md and README.zh-CN.md are present in package
    $readmeFiles = @("README.md", "README.zh-CN.md")
    foreach ($rm in $readmeFiles) {
        $sourceRm = Join-Path $SolutionDir $rm
        $destRm = Join-Path $outputDir $rm
        if ((Test-Path $sourceRm) -and (-not (Test-Path $destRm))) {
            Copy-Item -Path $sourceRm -Destination $destRm -Force
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

        # Ensure executable permissions for Unix binaries before archiving
        if (Get-Command chmod -ErrorAction SilentlyContinue) {
            $appBin = Join-Path $SourceDir "TaskScheduler.App"
            $cliBin = Join-Path $SourceDir "tscli"
            if (Test-Path $appBin) { & chmod +x $appBin }
            if (Test-Path $cliBin) { & chmod +x $cliBin }
        }

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
    try {
        Remove-Item -Recurse -Force $PublishBase -ErrorAction Stop
    } catch {
        Write-Warning "Could not completely clean publish directory (some files may be in use): $_"
    }
}

Invoke-Tests

if ($All) {
    $targets = @("win-x64", "linux-x64", "osx-x64")
    # Auto-detect ARM64
    $isArm64 = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().Equals("Arm64", [StringComparison]::OrdinalIgnoreCase)
    if ($isArm64) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
            $targets = @("win-arm64")
        } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
            $targets = @("linux-arm64")
        } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
            $targets = @("osx-arm64")
        }
    }

    foreach ($rid in $targets) {
        Invoke-Build -Rid $rid
    }
} elseif ($Runtime -ne "") {
    Invoke-Build -Rid $Runtime
} else {
    # Auto-detect current platform
    $isArm64 = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().Equals("Arm64", [StringComparison]::OrdinalIgnoreCase)
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        $Runtime = if ($isArm64) { "win-arm64" } else { "win-x64" }
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        $Runtime = if ($isArm64) { "linux-arm64" } else { "linux-x64" }
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        $Runtime = if ($isArm64) { "osx-arm64" } else { "osx-x64" }
    } else {
        Write-Error "Cannot detect platform. Please specify -Runtime explicitly."
        exit 1
    }
    Invoke-Build -Rid $Runtime
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
