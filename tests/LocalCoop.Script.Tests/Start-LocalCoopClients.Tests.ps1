Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$scriptPath = Join-Path $repoRoot 'Start-LocalCoopClients.ps1'

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

function Assert-Throws([scriptblock]$scriptBlock, [string]$expectedText, [string]$message) {
    try {
        & $scriptBlock
    }
    catch {
        if ($_.Exception.Message.Contains($expectedText)) {
            return
        }

        throw "$message Expected exception containing '$expectedText' but got '$($_.Exception.Message)'."
    }

    throw "$message Expected exception containing '$expectedText' but no exception was thrown."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('localcoop-script-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $harnessArgs = Format-LocalCoopHarnessPreparationArgumentList `
        -ConfigRoot '.localcoop-clients' `
        -ClientCount 4 `
        -SessionId 'local-test5' `
        -Port 38993 `
        -GameExecutablePath 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe' `
        -ControllerDevices '0,1,none,3'

    Assert-SequenceEqual $harnessArgs @(
        'run',
        '--no-restore',
        '--project',
        'tools\LocalCoop.MultiClientHarness',
        '--',
        'prepare-clients',
        '.localcoop-clients',
        '4',
        'local-test5',
        '38993',
        'D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe',
        '0,1,none,3'
    ) 'Harness preparation should pass client count and controller overrides.'

    $clientDirectories = Get-LocalCoopClientConfigDirectories -ConfigRoot '.localcoop-clients' -ClientCount 4
    Assert-SequenceEqual $clientDirectories @(
        (Join-Path '.localcoop-clients' 'client-0'),
        (Join-Path '.localcoop-clients' 'client-1'),
        (Join-Path '.localcoop-clients' 'client-2'),
        (Join-Path '.localcoop-clients' 'client-3')
    ) 'Client directory list should include every generated client.'

    Assert-Throws { Assert-LocalCoopClientCount -ClientCount 1 } '2 through 4' 'Client count validation should reject values below 2.'
    Assert-Throws { Assert-LocalCoopClientCount -ClientCount 5 } '2 through 4' 'Client count validation should reject values above 4.'

    $gameRoot = Join-Path $tempRoot 'game'
    $modDirectory = Join-Path $gameRoot 'mods\LocalCoop'
    New-Item -ItemType Directory -Path $modDirectory -Force | Out-Null
    $ownedLogFiles = @(
        'localcoop-host-0-events.txt',
        'localcoop-client-1-events.txt',
        'localcoop-client-2-events.txt',
        'localcoop-client-3-events.txt',
        'localcoop-transport-probe-client-2.txt',
        'localcoop-transport-probe-client-3.txt'
    )

    foreach ($fileName in $ownedLogFiles) {
        Set-Content -LiteralPath (Join-Path $modDirectory $fileName) -Value 'old log data'
    }

    Clear-LocalCoopLaunchLogs -GameRoot $gameRoot

    foreach ($fileName in $ownedLogFiles) {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $modDirectory $fileName))) "Launch cleanup should remove $fileName."
    }

    Write-Host 'Start-LocalCoopClients.Tests.ps1 passed.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
