$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$stateDir = Join-Path $repoRoot '.rsh-multilang-smoke'
$shellDll = Join-Path $repoRoot 'src\ReaperShell\bin\Debug\net10.0\ReaperShell.dll'
$dotnetHome = Join-Path $stateDir 'dotnet-home'
$nuGetPackages = Join-Path $dotnetHome 'packages'
$appData = Join-Path $dotnetHome 'AppData\Roaming'
$scriptPath = Join-Path $repoRoot 'scripts\smoke-multilang.rsh'

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

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Output,
        [Parameter(Mandatory = $true)]
        [string] $Expected
    )

    if (-not $Output.Contains($Expected)) {
        throw "Expected output to contain: $Expected"
    }
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $shellDll)) {
        throw "ReaperShell build output not found: $shellDll"
    }

    Reset-SmokeState

    $output = & dotnet $shellDll --state-dir $stateDir --script $scriptPath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Multi-language smoke script failed.`n$output"
    }

    Assert-Contains $output 'HelloCSharpCommand |'
    Assert-Contains $output 'HelloFSharpCommand |'
    Assert-Contains $output 'HelloVbCommand |'
    Assert-Contains $output 'Loaded commands: hello-csharp, hello-fsharp, hello-vb'
    Assert-Contains $output 'Hello from the C# command pack.'
    Assert-Contains $output 'Hello from the F# command pack.'
    Assert-Contains $output 'Hello from the VB.NET command pack.'
}
finally {
    Pop-Location
}
