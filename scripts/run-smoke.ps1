$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$stateDir = Join-Path $repoRoot '.rsh-smoke'
$shellDll = Join-Path $repoRoot 'src\ReaperShell\bin\Debug\net10.0\ReaperShell.dll'
$dotnetHome = Join-Path $stateDir 'dotnet-home'
$nuGetPackages = Join-Path $dotnetHome 'packages'
$appData = Join-Path $dotnetHome 'AppData\Roaming'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action
    )

    Write-Host "==> $Description"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Initialize-SmokeEnvironment {
    New-Item -ItemType Directory -Force -Path $nuGetPackages | Out-Null
    New-Item -ItemType Directory -Force -Path $appData | Out-Null

    $env:APPDATA = $appData
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:NUGET_PACKAGES = $nuGetPackages
}

function Reset-SmokeState {
    if (Test-Path -LiteralPath $stateDir) {
        Remove-Item -LiteralPath $stateDir -Recurse -Force
    }

    Initialize-SmokeEnvironment
}

Push-Location $repoRoot
try {
    Reset-SmokeState

    Invoke-Step 'Building ReaperShell' {
        dotnet build ReaperShell.slnx
    }

    Reset-SmokeState
    Invoke-Step 'Running sample smoke script' {
        dotnet $shellDll --state-dir $stateDir --script (Join-Path $repoRoot 'scripts\smoke-sample.rsh')
    }

    Reset-SmokeState
    Invoke-Step 'Running generated-pack smoke script' {
        dotnet $shellDll --state-dir $stateDir --script (Join-Path $repoRoot 'scripts\smoke-generated.rsh')
    }

    Reset-SmokeState
    Invoke-Step 'Running repo lifecycle smoke script' {
        dotnet $shellDll --state-dir $stateDir --script (Join-Path $repoRoot 'scripts\smoke-repo-lifecycle.rsh')
    }
}
finally {
    Pop-Location
}
