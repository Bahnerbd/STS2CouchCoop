param(
    [string]$RepoRoot = $PSScriptRoot,
    [string]$GameRoot,
    [string]$ConfigRoot,
    [string]$BrokerConfigPath,
    [string]$DefaultSessionId = 'local-test',
    [string]$DefaultHost = '127.0.0.1',
    [int]$DefaultPort = 38989,
    [int]$BrokerStartupTimeoutSeconds = 60,
    [switch]$SkipClientLaunch,
    [switch]$NoWaitForBroker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-LocalCoopBrokerConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,
        [Parameter(Mandatory = $true)]
        [string]$DefaultSessionId,
        [Parameter(Mandatory = $true)]
        [string]$DefaultHost,
        [Parameter(Mandatory = $true)]
        [int]$DefaultPort
    )

    $sessionId = $DefaultSessionId
    $hostName = $DefaultHost
    $port = $DefaultPort
    $source = 'defaults'

    if (Test-Path -LiteralPath $ConfigPath) {
        $source = (Resolve-Path -LiteralPath $ConfigPath).Path
        $values = @{}

        foreach ($line in Get-Content -LiteralPath $ConfigPath) {
            $trimmed = $line.Trim()
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
                continue
            }

            $separator = $trimmed.IndexOf('=')
            if ($separator -lt 1) {
                continue
            }

            $key = $trimmed.Substring(0, $separator).Trim()
            $value = $trimmed.Substring($separator + 1).Trim()
            $values[$key] = $value
        }

        if ($values.ContainsKey('sessionId') -and -not [string]::IsNullOrWhiteSpace($values['sessionId'])) {
            $sessionId = $values['sessionId']
        }

        if ($values.ContainsKey('endpoint') -and -not [string]::IsNullOrWhiteSpace($values['endpoint'])) {
            $endpoint = $values['endpoint']
            $portSeparator = $endpoint.LastIndexOf(':')
            if ($portSeparator -gt 0 -and $portSeparator -lt ($endpoint.Length - 1)) {
                $candidateHost = $endpoint.Substring(0, $portSeparator)
                $candidatePortText = $endpoint.Substring($portSeparator + 1)
                $candidatePort = 0
                if ([int]::TryParse($candidatePortText, [ref]$candidatePort) -and $candidatePort -gt 0 -and $candidatePort -le 65535) {
                    $hostName = $candidateHost
                    $port = $candidatePort
                }
            }
        }
    }

    [pscustomobject]@{
        SessionId = $sessionId
        Host = $hostName
        Port = $port
        Source = $source
    }
}

function Format-LocalCoopBrokerArgumentList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    @('src\LocalCoop.Broker.Cli\bin\Debug\net9.0\LocalCoop.Broker.Cli.dll', $SessionId, $Port.ToString())
}

function Test-LocalCoopTcpPort {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [int]$TimeoutMilliseconds = 750
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $connect.AsyncWaitHandle.WaitOne($TimeoutMilliseconds)) {
            return $false
        }

        $client.EndConnect($connect)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Wait-LocalCoopBroker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (Test-LocalCoopTcpPort -HostName $HostName -Port $Port) {
            return $true
        }

        Start-Sleep -Milliseconds 250
    }

    return $false
}

function ConvertTo-LocalCoopFileToken {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        if ($invalid -contains $character) {
            [void]$builder.Append('_')
        }
        else {
            [void]$builder.Append($character)
        }
    }

    $builder.ToString()
}

function ConvertTo-LocalCoopCommandLineArgument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    '"' + $Argument.Replace('"', '\"') + '"'
}

function ConvertTo-LocalCoopPowerShellSingleQuoted {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    "'" + $Value.Replace("'", "''") + "'"
}

function Write-LocalCoopBrokerLauncherScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    $sessionToken = ConvertTo-LocalCoopFileToken -Value $SessionId
    $runtimeRoot = Join-Path $RepoRoot '.localcoop-runtime'
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

    $launcherPath = Join-Path $runtimeRoot ("start-broker-{0}-{1}.ps1" -f $sessionToken, $Port)
    $logPath = Join-Path $runtimeRoot ("broker-{0}-{1}.log" -f $sessionToken, $Port)
    $arguments = Format-LocalCoopBrokerArgumentList -SessionId $SessionId -Port $Port
    $quotedArguments = ($arguments | ForEach-Object { ConvertTo-LocalCoopPowerShellSingleQuoted -Value $_ }) -join ', '

    $content = @(
        'Set-StrictMode -Version Latest'
        '$ErrorActionPreference = ''Stop'''
        ('Set-Location -LiteralPath {0}' -f (ConvertTo-LocalCoopPowerShellSingleQuoted -Value $RepoRoot))
        ('$brokerArguments = @({0})' -f $quotedArguments)
        ('& dotnet @brokerArguments *> {0}' -f (ConvertTo-LocalCoopPowerShellSingleQuoted -Value $logPath))
    )

    Set-Content -LiteralPath $launcherPath -Value $content
    $launcherPath
}

function New-LocalCoopBrokerStartInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    $launcherPath = Write-LocalCoopBrokerLauncherScript -RepoRoot $RepoRoot -SessionId $SessionId -Port $Port
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $launcherPath
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = (($arguments | ForEach-Object { ConvertTo-LocalCoopCommandLineArgument -Argument $_ }) -join ' ')
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $startInfo
}

function Invoke-LocalCoopBrokerBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    Push-Location -LiteralPath $RepoRoot
    try {
        $buildOutput = & dotnet build 'src\LocalCoop.Broker.Cli\LocalCoop.Broker.Cli.csproj' --no-restore 2>&1
        foreach ($line in $buildOutput) {
            Write-Host $line
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Broker CLI build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Start-LocalCoopBrokerProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    Invoke-LocalCoopBrokerBuild -RepoRoot $RepoRoot
    $startInfo = New-LocalCoopBrokerStartInfo -RepoRoot $RepoRoot -SessionId $SessionId -Port $Port
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'Failed to start LocalCoop broker process.'
    }

    $process
}

function Stop-LocalCoopProcessTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    if ($Process.HasExited) {
        return
    }

    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($Process.Id)" -ErrorAction SilentlyContinue
    foreach ($child in $children) {
        $childProcess = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
        if ($childProcess -is [System.Diagnostics.Process]) {
            Stop-LocalCoopProcessTree -Process $childProcess
        }
    }

    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $Process.WaitForExit(5000) -and -not $Process.HasExited) {
        $Process.Kill()
        [void]$Process.WaitForExit(5000)
    }
}

function Wait-LocalCoopBrokerProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    $script:LocalCoopBrokerCancellationRequested = $false
    $cancelHandler = [ConsoleCancelEventHandler]{
        param($sender, $eventArgs)
        $eventArgs.Cancel = $true
        $script:LocalCoopBrokerCancellationRequested = $true
    }

    [Console]::CancelKeyPress += $cancelHandler
    try {
        while (-not $Process.HasExited) {
            if ($script:LocalCoopBrokerCancellationRequested) {
                Write-Host 'Ctrl+C received; stopping LocalCoop broker and exiting.'
                Stop-LocalCoopProcessTree -Process $Process
                break
            }

            Start-Sleep -Milliseconds 200
        }
    }
    finally {
        [Console]::CancelKeyPress -= $cancelHandler
        $script:LocalCoopBrokerCancellationRequested = $false
    }
}

function Invoke-LocalCoopTwoClientPreparation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ConfigRoot,
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$GameExecutablePath
    )

    $arguments = Format-LocalCoopHarnessPreparationArgumentList `
        -ConfigRoot $ConfigRoot `
        -SessionId $SessionId `
        -Port $Port `
        -GameExecutablePath $GameExecutablePath

    Push-Location -LiteralPath $RepoRoot
    try {
        $prepareOutput = & dotnet @arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            foreach ($line in $prepareOutput) {
                Write-Host $line
            }

            throw "Two-client harness preparation failed with exit code $LASTEXITCODE."
        }

        Write-Host 'Two-client config prepared.'
    }
    finally {
        Pop-Location
    }
}

function Format-LocalCoopHarnessPreparationArgumentList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigRoot,
        [Parameter(Mandatory = $true)]
        [string]$SessionId,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$GameExecutablePath
    )

    @(
        'run',
        '--no-restore',
        '--project',
        'tools\LocalCoop.MultiClientHarness',
        '--',
        'prepare-two-client',
        $ConfigRoot,
        $SessionId,
        $Port.ToString(),
        $GameExecutablePath
    )
}

function New-LocalCoopClientStartInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$GameExecutablePath,
        [Parameter(Mandatory = $true)]
        [string]$GameRoot,
        [Parameter(Mandatory = $true)]
        [string]$ClientConfigDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $GameExecutablePath
    $startInfo.WorkingDirectory = $GameRoot
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables['LOCALCOOP_CONFIG_DIR'] = $ClientConfigDirectory
    $startInfo
}

function Start-LocalCoopClientProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$GameExecutablePath,
        [Parameter(Mandatory = $true)]
        [string]$GameRoot,
        [Parameter(Mandatory = $true)]
        [string]$ClientConfigDirectory
    )

    $startInfo = New-LocalCoopClientStartInfo `
        -GameExecutablePath $GameExecutablePath `
        -GameRoot $GameRoot `
        -ClientConfigDirectory $ClientConfigDirectory

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start STS2 client for config $ClientConfigDirectory."
    }

    $process
}

function Invoke-LocalCoopTwoClientStartup {
    [CmdletBinding()]
    param(
        [string]$RepoRoot = $PSScriptRoot,
        [string]$GameRoot,
        [string]$ConfigRoot,
        [string]$BrokerConfigPath,
        [string]$DefaultSessionId = 'local-test',
        [string]$DefaultHost = '127.0.0.1',
        [int]$DefaultPort = 38989,
        [int]$BrokerStartupTimeoutSeconds = 60,
        [switch]$SkipClientLaunch,
        [switch]$NoWaitForBroker
    )

    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $GameRoot = (Resolve-Path -LiteralPath (Join-Path $resolvedRepoRoot '..')).Path
    }
    else {
        $GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
    }

    if ([string]::IsNullOrWhiteSpace($ConfigRoot)) {
        $ConfigRoot = Join-Path $resolvedRepoRoot '.localcoop-clients'
    }

    if ([string]::IsNullOrWhiteSpace($BrokerConfigPath)) {
        $BrokerConfigPath = Join-Path $GameRoot 'mods\LocalCoop\enable-local-broker.txt'
    }

    $gameExecutablePath = Join-Path $GameRoot 'SlayTheSpire2.exe'
    if (-not (Test-Path -LiteralPath $gameExecutablePath)) {
        throw "Game executable not found: $gameExecutablePath"
    }

    $broker = Read-LocalCoopBrokerConfig `
        -ConfigPath $BrokerConfigPath `
        -DefaultSessionId $DefaultSessionId `
        -DefaultHost $DefaultHost `
        -DefaultPort $DefaultPort

    Write-Host ("Broker config: sessionId={0} endpoint={1}:{2} source={3}" -f $broker.SessionId, $broker.Host, $broker.Port, $broker.Source)

    $brokerUp = Test-LocalCoopTcpPort -HostName $broker.Host -Port $broker.Port
    $startedBrokerProcess = $null
    if ($brokerUp) {
        Write-Host ("Broker already listening on {0}:{1}." -f $broker.Host, $broker.Port)
    }
    else {
        Write-Host ("Broker is not listening on {0}:{1}; starting it now." -f $broker.Host, $broker.Port)
        $startedBrokerProcess = Start-LocalCoopBrokerProcess -RepoRoot $resolvedRepoRoot -SessionId $broker.SessionId -Port $broker.Port
        Write-Host ("Started broker process id {0}." -f $startedBrokerProcess.Id)

        if (-not (Wait-LocalCoopBroker -HostName $broker.Host -Port $broker.Port -TimeoutSeconds $BrokerStartupTimeoutSeconds)) {
            throw "Broker did not start listening on $($broker.Host):$($broker.Port) within $BrokerStartupTimeoutSeconds seconds."
        }
    }

    Invoke-LocalCoopTwoClientPreparation `
        -RepoRoot $resolvedRepoRoot `
        -ConfigRoot $ConfigRoot `
        -SessionId $broker.SessionId `
        -Port $broker.Port `
        -GameExecutablePath $gameExecutablePath

    if ($SkipClientLaunch) {
        Write-Host 'Prepared two-client config; client launch skipped.'
        if ($startedBrokerProcess -is [System.Diagnostics.Process] -and -not $NoWaitForBroker) {
            Write-Host 'Broker was started by this script. Leave this window open; press Ctrl+C to stop it.'
            Wait-LocalCoopBrokerProcess -Process $startedBrokerProcess
        }

        return
    }

    $clientDirectories = @(
        Join-Path $ConfigRoot 'client-0'
        Join-Path $ConfigRoot 'client-1'
    )

    foreach ($clientDirectory in $clientDirectories) {
        if (-not (Test-Path -LiteralPath (Join-Path $clientDirectory 'enable-local-broker.txt'))) {
            throw "Client config missing: $clientDirectory"
        }

        $clientProcess = Start-LocalCoopClientProcess `
            -GameExecutablePath $gameExecutablePath `
            -GameRoot $GameRoot `
            -ClientConfigDirectory $clientDirectory

        Write-Host ("Started STS2 client process id {0} using config {1}." -f $clientProcess.Id, $clientDirectory)
    }

    if ($startedBrokerProcess -is [System.Diagnostics.Process] -and -not $NoWaitForBroker) {
        Write-Host 'Broker was started by this script. Leave this window open while the clients are running; press Ctrl+C to stop it.'
        Wait-LocalCoopBrokerProcess -Process $startedBrokerProcess
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-LocalCoopTwoClientStartup @PSBoundParameters
}
