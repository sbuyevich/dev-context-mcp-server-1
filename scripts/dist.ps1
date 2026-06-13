[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$runtimeIdentifier = "win-x64"
$configuration = "Release"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$globalJsonPath = Join-Path $repositoryRoot "global.json"
$toolsRoot = Join-Path $repositoryRoot "artifacts\tools"
$localDotNetRoot = Join-Path $toolsRoot "dotnet"
$localDotNet = Join-Path $localDotNetRoot "dotnet.exe"
$dotNetInstallScript = Join-Path $toolsRoot "dotnet-install.ps1"
$distributionRoot = Join-Path $repositoryRoot "artifacts\dist\$runtimeIdentifier"
$serverOutput = Join-Path $distributionRoot "server"
$indexerOutput = Join-Path $distributionRoot "indexer"
$dataOutput = Join-Path $distributionRoot "data"
$stagingRoot = Join-Path $distributionRoot ".staging"

function Assert-DistributionPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $expectedPrefix = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "artifacts\dist")) +
        [System.IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify distribution path outside '$expectedPrefix': '$resolvedPath'."
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $script:dotnetCommand @Arguments
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
    }
}

function Test-DotNetSdk {
    param(
        [Parameter(Mandatory)]
        [string] $Command
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $Command --version 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference

    return [pscustomobject]@{
        IsAvailable = $exitCode -eq 0
        Version = if ($exitCode -eq 0) { @($output)[-1].ToString().Trim() } else { $null }
    }
}

function Install-LocalDotNetSdk {
    param(
        [Parameter(Mandatory)]
        [string] $Version
    )

    New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
    Write-Host "Downloading the official .NET install script..."

    $previousProgressPreference = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        Invoke-WebRequest `
            -Uri "https://dot.net/v1/dotnet-install.ps1" `
            -OutFile $dotNetInstallScript `
            -UseBasicParsing
    }
    catch {
        throw "Failed to download the official .NET install script: $($_.Exception.Message)"
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }

    Write-Host "Installing .NET SDK $Version into '$localDotNetRoot'..."
    & $dotNetInstallScript `
        -Version $Version `
        -InstallDir $localDotNetRoot `
        -Architecture "x64" `
        -NoPath

    $installedSdk = Test-DotNetSdk -Command $localDotNet
    if (-not $installedSdk.IsAvailable -or $installedSdk.Version -ne $Version) {
        throw "The local .NET SDK installation did not provide the requested SDK $Version."
    }
}

function Set-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    $json = $Value | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText(
        $Path,
        $json + [System.Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function New-DataLayout {
    param(
        [Parameter(Mandatory)]
        [string] $Root
    )

    @(
        "database",
        "nugets",
        "nuget-repositories\qa",
        "nuget-repositories\prod"
    ) | ForEach-Object {
        New-Item -ItemType Directory -Path (Join-Path $Root $_) -Force | Out-Null
    }
}

function New-DistributionZip {
    param(
        [Parameter(Mandatory)]
        [string] $ApplicationDirectory,

        [Parameter(Mandatory)]
        [string] $ApplicationName,

        [Parameter(Mandatory)]
        [string] $LauncherPath,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $packageRoot = Join-Path $stagingRoot $ApplicationName
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item `
        -Path $ApplicationDirectory `
        -Destination (Join-Path $packageRoot $ApplicationName) `
        -Recurse
    Copy-Item -LiteralPath $LauncherPath -Destination $packageRoot
    New-DataLayout -Root (Join-Path $packageRoot "data")

    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $packageRoot,
        $DestinationPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected distribution file was not created: '$Path'."
    }
}

function Assert-ZipLayout {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string[]] $RequiredEntries
    )

    Assert-FileExists -Path $Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = $archive.Entries.FullName |
            ForEach-Object { $_.Replace("\", "/") }
        foreach ($requiredEntry in $RequiredEntries) {
            if ($entries -notcontains $requiredEntry) {
                throw "Distribution archive '$Path' is missing '$requiredEntry'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
    throw "Could not find global.json at '$globalJsonPath'."
}

$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$requiredSdk = $globalJson.sdk.version
$script:dotnetCommand = $null

$systemDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($systemDotNet) {
    $systemSdk = Test-DotNetSdk -Command $systemDotNet.Source
    if ($systemSdk.IsAvailable) {
        $script:dotnetCommand = $systemDotNet.Source
    }
}

if (-not $script:dotnetCommand -and (Test-Path -LiteralPath $localDotNet -PathType Leaf)) {
    $localSdk = Test-DotNetSdk -Command $localDotNet
    if ($localSdk.IsAvailable) {
        $script:dotnetCommand = $localDotNet
    }
}

if (-not $script:dotnetCommand) {
    Install-LocalDotNetSdk -Version $requiredSdk
    $script:dotnetCommand = $localDotNet
    $env:DOTNET_ROOT = $localDotNetRoot
}

$selectedSdk = Test-DotNetSdk -Command $script:dotnetCommand
Write-Host "Using .NET SDK $($selectedSdk.Version) from '$script:dotnetCommand'"
Write-Host "Creating self-contained $runtimeIdentifier distributions..."

Assert-DistributionPath -Path $distributionRoot
if (Test-Path -LiteralPath $distributionRoot) {
    Remove-Item -LiteralPath $distributionRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $distributionRoot -Force | Out-Null
New-DataLayout -Root $dataOutput

$serverProject = Join-Path $repositoryRoot "src\DevContextMcp.Server\DevContextMcp.Server.csproj"
$indexerProject = Join-Path $repositoryRoot "src\DevContextMcp.Indexer\DevContextMcp.Indexer.csproj"

Invoke-DotNet -Arguments @(
    "publish",
    $serverProject,
    "--configuration", $configuration,
    "--runtime", $runtimeIdentifier,
    "--self-contained", "true",
    "--property:PublishSingleFile=false",
    "--output", $serverOutput
)

Invoke-DotNet -Arguments @(
    "publish",
    $indexerProject,
    "--configuration", $configuration,
    "--runtime", $runtimeIdentifier,
    "--self-contained", "true",
    "--property:PublishSingleFile=false",
    "--output", $indexerOutput
)

$serverSettingsPath = Join-Path $serverOutput "appsettings.json"
$serverSettings = Get-Content -LiteralPath $serverSettingsPath -Raw | ConvertFrom-Json
$serverSettings.DevContextMcp.DatabasePath = "../data/database/docs.db"
Set-JsonFile -Path $serverSettingsPath -Value $serverSettings

$indexerSettingsPath = Join-Path $indexerOutput "appsettings.json"
$indexerSettings = Get-Content -LiteralPath $indexerSettingsPath -Raw | ConvertFrom-Json
$indexerSettings.DevContextMcp.DatabasePath = "../data/database/docs.db"
$indexerSettings.DevContextMcp.NugetsPath = "../data/nugets"
foreach ($environment in $indexerSettings.DevContextMcp.Environments) {
    if ($environment.Name -eq "qa") {
        $environment.ServiceIndex = "../data/nuget-repositories/qa"
    }
    elseif ($environment.Name -eq "prod") {
        $environment.ServiceIndex = "../data/nuget-repositories/prod"
    }
}
Set-JsonFile -Path $indexerSettingsPath -Value $indexerSettings

$serverLauncher = @'
@echo off
setlocal
pushd "%~dp0"
"DevContextMcp.Server.exe" %*
set "exitCode=%ERRORLEVEL%"
popd
exit /b %exitCode%
'@

$indexerLauncher = @'
@echo off
setlocal
pushd "%~dp0"
"DevContextMcp.Indexer.exe" %*
set "exitCode=%ERRORLEVEL%"
popd
exit /b %exitCode%
'@

$rootServerLauncher = @'
@echo off
setlocal
pushd "%~dp0server"
"DevContextMcp.Server.exe" %*
set "exitCode=%ERRORLEVEL%"
popd
exit /b %exitCode%
'@

$rootIndexerLauncher = @'
@echo off
setlocal
pushd "%~dp0indexer"
"DevContextMcp.Indexer.exe" %*
set "exitCode=%ERRORLEVEL%"
popd
exit /b %exitCode%
'@

[System.IO.File]::WriteAllText(
    (Join-Path $serverOutput "server.cmd"),
    $serverLauncher + [System.Environment]::NewLine,
    [System.Text.Encoding]::ASCII)
[System.IO.File]::WriteAllText(
    (Join-Path $indexerOutput "indexer.cmd"),
    $indexerLauncher + [System.Environment]::NewLine,
    [System.Text.Encoding]::ASCII)

$rootServerLauncherPath = Join-Path $distributionRoot "server.cmd"
$rootIndexerLauncherPath = Join-Path $distributionRoot "indexer.cmd"
[System.IO.File]::WriteAllText(
    $rootServerLauncherPath,
    $rootServerLauncher + [System.Environment]::NewLine,
    [System.Text.Encoding]::ASCII)
[System.IO.File]::WriteAllText(
    $rootIndexerLauncherPath,
    $rootIndexerLauncher + [System.Environment]::NewLine,
    [System.Text.Encoding]::ASCII)

Assert-FileExists -Path (Join-Path $serverOutput "DevContextMcp.Server.exe")
Assert-FileExists -Path $serverSettingsPath
Assert-FileExists -Path (Join-Path $serverOutput "server.cmd")
Assert-FileExists -Path (Join-Path $indexerOutput "DevContextMcp.Indexer.exe")
Assert-FileExists -Path $indexerSettingsPath
Assert-FileExists -Path (Join-Path $indexerOutput "indexer.cmd")
Assert-FileExists -Path $rootServerLauncherPath
Assert-FileExists -Path $rootIndexerLauncherPath

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$serverZip = Join-Path $distributionRoot "DevContextMcp.Server-$runtimeIdentifier.zip"
$indexerZip = Join-Path $distributionRoot "DevContextMcp.Indexer-$runtimeIdentifier.zip"
New-DistributionZip `
    -ApplicationDirectory $serverOutput `
    -ApplicationName "server" `
    -LauncherPath $rootServerLauncherPath `
    -DestinationPath $serverZip
New-DistributionZip `
    -ApplicationDirectory $indexerOutput `
    -ApplicationName "indexer" `
    -LauncherPath $rootIndexerLauncherPath `
    -DestinationPath $indexerZip
Remove-Item -LiteralPath $stagingRoot -Recurse -Force

$dataEntries = @(
    "data/database/",
    "data/nugets/",
    "data/nuget-repositories/qa/",
    "data/nuget-repositories/prod/"
)
Assert-ZipLayout -Path $serverZip -RequiredEntries (@(
        "server.cmd",
        "server/DevContextMcp.Server.exe",
        "server/appsettings.json",
        "server/server.cmd"
    ) + $dataEntries)
Assert-ZipLayout -Path $indexerZip -RequiredEntries (@(
        "indexer.cmd",
        "indexer/DevContextMcp.Indexer.exe",
        "indexer/appsettings.json",
        "indexer/indexer.cmd"
    ) + $dataEntries)

Write-Host ""
Write-Host "Distribution completed:"
Write-Host "  $serverOutput"
Write-Host "  $indexerOutput"
Write-Host "  $serverZip"
Write-Host "  $indexerZip"
