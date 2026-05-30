Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$scriptPath = Join-Path $repoRoot 'Start-LocalCoopTwoClient.ps1'

if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Startup script not found: $scriptPath"
}

. $scriptPath

function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -ne $expected) {
        throw "$message Expected '$expected' but got '$actual'."
    }
}

function Assert-SequenceEqual($actual, $expected, [string]$message) {
    $actualText = ($actual -join '|')
    $expectedText = ($expected -join '|')
    if ($actualText -ne $expectedText) {
        throw "$message Expected '$expectedText' but got '$actualText'."
    }
}

function Assert-True($condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('localcoop-script-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $missingConfig = Join-Path $tempRoot 'missing-enable-local-broker.txt'
    $fallback = Read-LocalCoopBrokerConfig `
        -ConfigPath $missingConfig `
        -DefaultSessionId 'local-test' `
        -DefaultHost '127.0.0.1' `
        -DefaultPort 38989

    Assert-Equal $fallback.SessionId 'local-test' 'Missing config should use default session id.'
    Assert-Equal $fallback.Host '127.0.0.1' 'Missing config should use default host.'
    Assert-Equal $fallback.Port 38989 'Missing config should use default port.'
    Assert-Equal $fallback.Source 'defaults' 'Missing config should identify defaults as source.'

    $configPath = Join-Path $tempRoot 'enable-local-broker.txt'
    @(
        'role=host'
        'clientIndex=0'
        'endpoint=127.0.0.1:38993'
        'sessionId=local-test5'
    ) | Set-Content -LiteralPath $configPath

    $configured = Read-LocalCoopBrokerConfig `
        -ConfigPath $configPath `
        -DefaultSessionId 'local-test' `
        -DefaultHost '127.0.0.1' `
        -DefaultPort 38989

    Assert-Equal $configured.SessionId 'local-test5' 'Existing config should provide session id.'
    Assert-Equal $configured.Host '127.0.0.1' 'Existing config should provide endpoint host.'
    Assert-Equal $configured.Port 38993 'Existing config should provide endpoint port.'
    Assert-Equal $configured.Source $configPath 'Existing config should identify config path as source.'

    $args = Format-LocalCoopBrokerArgumentList -SessionId 'local-test5' -Port 38993
    Assert-SequenceEqual $args @('src\LocalCoop.Broker.Cli\bin\Debug\net9.0\LocalCoop.Broker.Cli.dll', 'local-test5', '38993') 'Broker argument list should launch the built CLI directly.'

    $harnessArgs = Format-LocalCoopHarnessPreparationArgumentList `
        -ConfigRoot '.localcoop-clients' `
        -SessionId 'local-test5' `
        -Port 38993 `
        -GameExecutablePath 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe'

    Assert-SequenceEqual $harnessArgs @(
        'run',
        '--no-restore',
        '--project',
        'tools\LocalCoop.MultiClientHarness',
        '--',
        'prepare-two-client',
        '.localcoop-clients',
        'local-test5',
        '38993',
        'D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe'
    ) 'Harness preparation should avoid implicit restore.'

    $brokerStartInfo = New-LocalCoopBrokerStartInfo `
        -RepoRoot $tempRoot `
        -SessionId 'local-test5' `
        -Port 38993

    Assert-Equal $brokerStartInfo.FileName 'powershell.exe' 'Broker start info should run a detached PowerShell launcher.'
    Assert-Equal $brokerStartInfo.WorkingDirectory $tempRoot 'Broker start info should run from the repo root.'
    Assert-Equal $brokerStartInfo.UseShellExecute $false 'Broker start info should avoid Start-Process shell environment handling.'
    Assert-Equal $brokerStartInfo.CreateNoWindow $true 'Broker start info should run without opening an extra console window.'
    Assert-Equal $brokerStartInfo.RedirectStandardOutput $false 'Broker launcher should redirect broker output itself.'
    Assert-Equal $brokerStartInfo.RedirectStandardError $false 'Broker launcher should redirect broker errors itself.'

    $launcherScript = Join-Path $tempRoot '.localcoop-runtime\start-broker-local-test5-38993.ps1'
    if (-not (Test-Path -LiteralPath $launcherScript)) {
        throw "Expected broker launcher script to exist at $launcherScript."
    }

    $sleepProcess = Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') `
        -WindowStyle Hidden `
        -PassThru

    try {
        Stop-LocalCoopProcessTree -Process $sleepProcess
        Assert-True ($sleepProcess.WaitForExit(5000)) 'Stop-LocalCoopProcessTree should stop the supplied process.'
    }
    finally {
        if (-not $sleepProcess.HasExited) {
            Stop-Process -Id $sleepProcess.Id -Force
        }
    }

    Write-Host 'Start-LocalCoopTwoClient.Tests.ps1 passed.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
