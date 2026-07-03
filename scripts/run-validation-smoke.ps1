$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$stateDir = Join-Path $repoRoot '.rsh-validation-smoke'
$shellDll = Join-Path $repoRoot 'src\ReaperShell\bin\Debug\net10.0\ReaperShell.dll'
$dotnetHome = Join-Path $stateDir 'dotnet-home'
$nuGetPackages = Join-Path $dotnetHome 'packages'
$appData = Join-Path $dotnetHome 'AppData\Roaming'
$samplePack = Join-Path $repoRoot 'sample-packs\hello-pack'

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

function Reset-ValidationState {
    if (Test-Path -LiteralPath $stateDir) {
        Remove-Item -LiteralPath $stateDir -Recurse -Force
    }

    Initialize-SmokeEnvironment
}

function Invoke-ExpectedFailure {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,
        [Parameter(Mandatory = $true)]
        [string] $CommandText
    )

    Write-Host "==> Expecting failure: $Description"
    & dotnet $shellDll --state-dir $stateDir --command $CommandText
    if ($LASTEXITCODE -eq 0) {
        throw "Expected failure but command succeeded: $CommandText"
    }
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $shellDll)) {
        throw "ReaperShell build output not found: $shellDll"
    }

    Reset-ValidationState

    $invalidRepoNames = @('../evil', '..\evil', 'foo/bar', 'foo\bar', '.', '..')
    foreach ($name in $invalidRepoNames) {
        Invoke-ExpectedFailure "repo add invalid name $name" "repo add $name `"$samplePack`""
        Invoke-ExpectedFailure "repo new invalid name $name" "repo new $name"
    }

    & dotnet $shellDll --state-dir $stateDir --command 'repo new forge-smoke'
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to create forge-smoke test repo.'
    }

    Invoke-ExpectedFailure 'command new rejects leading-digit name' 'command new forge-smoke 123-bad'
}
finally {
    Pop-Location
}
