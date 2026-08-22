# Requires: -Action build|rebuild|build-patcher
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('build', 'rebuild', 'build-patcher')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'

function Test-MsBuildHasCppTargets {
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$MsBuildPath
    )

    # VS installs MSBuild under <VSRoot>\MSBuild\Current\Bin\MSBuild.exe
    # C++ props live under <VSRoot>\MSBuild\Microsoft\VC\v*\Microsoft.Cpp.Default.props
    $binDir = Split-Path -Path $MsBuildPath -Parent
    $currentDir = Split-Path -Path $binDir -Parent
    $msbuildRoot = Split-Path -Path $currentDir -Parent
    $vcRoot = Join-Path $msbuildRoot 'Microsoft\VC'
    if (-not (Test-Path -Path $vcRoot)) {
        return $false
    }

    $props = Get-ChildItem -Path $vcRoot -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'Microsoft.Cpp.Default.props' } |
        Where-Object { Test-Path -Path $_ }

    return ($null -ne $props -and @($props).Count -gt 0)
}

function Find-MSBuild {
    if ($env:MSBUILD -and (Test-Path -LiteralPath $env:MSBUILD)) {
        return $env:MSBUILD
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $null
    }

    # Prefer installs that advertise the MSVC toolset; never fall back to an MSBuild
    # that lacks Microsoft.Cpp.Default.props (common with VS IDE installs without C++).
    $withVc = @(
        & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe'
    )
    $any = @(
        & $vswhere -products * -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe'
    )

    function Rank-MsBuildPath([string]$path) {
        if ($path -match '\\18\\') { return 180 }
        if ($path -match '\\2022\\') { return 170 }
        if ($path -match '\\2019\\') { return 160 }
        if ($path -match '\\2017\\') { return 150 }
        return 0
    }

    $candidates = @($withVc + $any) |
        Where-Object { $_ } |
        Select-Object -Unique |
        Where-Object { Test-MsBuildHasCppTargets -MsBuildPath $_ }

    if (-not $candidates -or $candidates.Count -eq 0) {
        return $null
    }

    return $candidates | Sort-Object { Rank-MsBuildPath $_ } -Descending | Select-Object -First 1
}

function Invoke-PatcherBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Msb,
        [switch]$Rebuild
    )

    # Building the .vcxproj directly does not set SolutionDir; the project uses it for
    # OutDir and lib paths (sqlite3.lib). Trailing slash matches MSBuild solution builds.
    $solutionDir = (Join-Path $repoRoot '') -replace '/', '\'
    if (-not $solutionDir.EndsWith('\')) {
        $solutionDir += '\'
    }

    $msbuildArgs = @(
        'src\KotorPatcher\KotorPatcher.vcxproj',
        '/p:Configuration=Debug',
        '/p:Platform=Win32',
        "/p:SolutionDir=$solutionDir",
        '/m'
    )
    if ($Rebuild) {
        $msbuildArgs += '/t:Rebuild'
    }

    # Pipe to Out-Host so stdout is not captured by callers that assign the return value
    & $Msb @msbuildArgs 2>&1 | Out-Host
    return ($LASTEXITCODE -eq 0)
}

function Invoke-LauncherBuild {
    param([switch]$Rebuild)

    $dotnetArgs = @(
        'build',
        'src\KPatchLauncher\KPatchLauncher.csproj',
        '-c', 'Debug'
    )
    if ($Rebuild) {
        $dotnetArgs += '--no-incremental'
    }

    & dotnet @dotnetArgs 2>&1 | Out-Host
    return ($LASTEXITCODE -eq 0)
}

$msb = Find-MSBuild
if (-not $msb) {
    Write-Error 'MSBuild not found. Install VS 2022 Build Tools (C++ v143 + MSBuild) or set MSBUILD to MSBuild.exe.'
    exit 1
}

Write-Host "Using MSBuild: $msb"

$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repoRoot
$patcherDll = Join-Path $repoRoot 'bin\Debug\KotorPatcher.dll'

switch ($Action) {
    'build-patcher' {
        if (-not (Invoke-PatcherBuild -Msb $msb)) {
            Write-Host ''
            Write-Host 'KotorPatcher build failed. Install the Desktop development with C++ workload'
            Write-Host '(MSVC v143 / VS 2022 toolset) or set MSBUILD to an MSBuild that has those tools.'
            exit 1
        }
        exit 0
    }
    'rebuild' {
        $patcherOk = Invoke-PatcherBuild -Msb $msb -Rebuild
        $launcherOk = Invoke-LauncherBuild -Rebuild
        if (-not $launcherOk) { exit 1 }
        if (-not $patcherOk) {
            if (Test-Path -LiteralPath $patcherDll) {
                Write-Warning "KotorPatcher rebuild failed; using existing $patcherDll. Install C++ v143 tools for native rebuilds."
                exit 0
            }
            Write-Host 'KotorPatcher rebuild failed and bin\Debug\KotorPatcher.dll is missing.'
            Write-Host 'Install Desktop development with C++ (MSVC v143) or set MSBUILD appropriately.'
            exit 1
        }
        exit 0
    }
    'build' {
        $patcherOk = Invoke-PatcherBuild -Msb $msb
        $launcherOk = Invoke-LauncherBuild
        if (-not $launcherOk) { exit 1 }
        if (-not $patcherOk) {
            if (Test-Path -LiteralPath $patcherDll) {
                Write-Warning "KotorPatcher build failed; using existing $patcherDll. Install C++ v143 tools for native rebuilds."
                exit 0
            }
            Write-Host 'KotorPatcher build failed and bin\Debug\KotorPatcher.dll is missing.'
            Write-Host 'Install Desktop development with C++ (MSVC v143) or set MSBUILD appropriately.'
            exit 1
        }
        exit 0
    }
}
