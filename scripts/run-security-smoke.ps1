$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$stateDir = Join-Path $repoRoot '.rsh-security-smoke'
$shellDll = Join-Path $repoRoot 'src\ReaperShell\bin\Debug\net10.0\ReaperShell.dll'
$dotnetHome = Join-Path $stateDir 'dotnet-home'
$nuGetPackages = Join-Path $dotnetHome 'packages'
$appData = Join-Path $dotnetHome 'AppData\Roaming'
$fixtureRoot = Join-Path $stateDir 'fixtures'

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

function Reset-SecurityState {
    if (Test-Path -LiteralPath $stateDir) {
        Remove-Item -LiteralPath $stateDir -Recurse -Force
    }

    Initialize-SmokeEnvironment
    New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
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

function Invoke-ExpectedSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,
        [Parameter(Mandatory = $true)]
        [string] $CommandText
    )

    Write-Host "==> $Description"
    & dotnet $shellDll --state-dir $stateDir --command $CommandText
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed unexpectedly: $CommandText"
    }
}

function New-MaliciousPack {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepoPath,
        [Parameter(Mandatory = $true)]
        [string] $CommandsPathValue
    )

    New-Item -ItemType Directory -Force -Path $RepoPath | Out-Null
    $escapedCommandsPathValue = $CommandsPathValue.Replace('\', '\\')
    @"
{
  "id": "evil-pack",
  "name": "Evil Pack",
  "description": "Should not be allowed to escape.",
  "commandsPath": "$escapedCommandsPathValue"
}
"@ | Set-Content -LiteralPath (Join-Path $RepoPath 'shellpack.json')
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $shellDll)) {
        throw "ReaperShell build output not found: $shellDll"
    }

    Reset-SecurityState

    $relativeOutsideRoot = Join-Path $fixtureRoot 'outside-commands'
    $relativeRepoRoot = Join-Path $fixtureRoot 'evil-relative'
    New-Item -ItemType Directory -Force -Path $relativeOutsideRoot | Out-Null
    New-MaliciousPack -RepoPath $relativeRepoRoot -CommandsPathValue '../outside-commands'

    Invoke-ExpectedSuccess 'Adding malicious relative-pack repo' "repo add evil-relative `"$relativeRepoRoot`""
    Invoke-ExpectedSuccess 'Trusting malicious relative-pack repo' 'repo trust evil-relative'
    Invoke-ExpectedFailure 'repo build evil-relative' 'repo build evil-relative'
    Invoke-ExpectedFailure 'repo load evil-relative' 'repo load evil-relative'
    Invoke-ExpectedFailure 'repo reload evil-relative' 'repo reload evil-relative'
    Invoke-ExpectedFailure 'command list evil-relative' 'command list evil-relative'
    Invoke-ExpectedFailure 'command new evil-relative should-not-exist' 'command new evil-relative should-not-exist'

    $absoluteOutsideRoot = Join-Path $fixtureRoot 'outside-absolute'
    $absoluteRepoRoot = Join-Path $fixtureRoot 'evil-absolute'
    New-Item -ItemType Directory -Force -Path $absoluteOutsideRoot | Out-Null
    New-MaliciousPack -RepoPath $absoluteRepoRoot -CommandsPathValue ([System.IO.Path]::GetFullPath($absoluteOutsideRoot))

    Invoke-ExpectedSuccess 'Adding malicious absolute-pack repo' "repo add evil-absolute `"$absoluteRepoRoot`""
    Invoke-ExpectedSuccess 'Trusting malicious absolute-pack repo' 'repo trust evil-absolute'
    Invoke-ExpectedFailure 'repo build evil-absolute' 'repo build evil-absolute'
    Invoke-ExpectedFailure 'repo load evil-absolute' 'repo load evil-absolute'
}
finally {
    Pop-Location
}
