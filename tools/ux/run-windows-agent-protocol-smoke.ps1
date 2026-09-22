[CmdletBinding()]
param(
    [string]$ProcessName = 'Xueqing.Windows',
    [string]$ProtocolUri = 'xueqing://today',
    [string]$DiagnosticOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Stop-XueqingProcesses {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    do {
        $remaining = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Unable to stop existing $ProcessName process(es) before protocol probe."
}

function Invoke-XueqingProtocol {
    param([Parameter(Mandatory = $true)][string]$Uri)

    # Explorer performs normal Windows shell protocol resolution against the
    # installed package registration. This deliberately does not launch the
    # installed exe directly.
    Start-Process -FilePath 'explorer.exe' -ArgumentList @($Uri) | Out-Null
}

function Get-XueqingProcesses {
    return @(
        Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Sort-Object Id
    )
}

function Find-AutomationId {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Wait-ForSignedOutAuthBoundary {
    param([int]$TimeoutSeconds = 30)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($process in Get-XueqingProcesses) {
            $process.Refresh()
            if ($process.MainWindowHandle -eq 0) {
                continue
            }

            try {
                $root = [System.Windows.Automation.AutomationElement]::FromHandle(
                    [IntPtr]$process.MainWindowHandle)
                $email = Find-AutomationId -Root $root -AutomationId 'AuthEmail'
                $submit = Find-AutomationId -Root $root -AutomationId 'AuthSubmit'
                if ($null -ne $email -and $null -ne $submit) {
                    return [pscustomobject]@{
                        ProcessId = $process.Id
                        WindowTitle = $process.MainWindowTitle
                    }
                }
            }
            catch {
                # WinUI can briefly replace/initialize its root automation
                # element while the window is coming up. Retry until timeout.
            }
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Protocol activation did not reach the normal signed-out Auth UI within $TimeoutSeconds seconds."
}

$diagnosticDirectory = if ([string]::IsNullOrWhiteSpace($DiagnosticOutput)) {
    $null
} else {
    Split-Path -Parent $DiagnosticOutput
}

if ($diagnosticDirectory) {
    New-Item -ItemType Directory -Force -Path $diagnosticDirectory | Out-Null
}

try {
    Stop-XueqingProcesses

    Invoke-XueqingProtocol -Uri $ProtocolUri
    $auth = Wait-ForSignedOutAuthBoundary
    $cold = @(Get-XueqingProcesses)
    if ($cold.Count -lt 1) {
        throw 'Protocol activation did not create an installed Xueqing process.'
    }

    $coldIds = @($cold | Select-Object -ExpandProperty Id)

    Invoke-XueqingProtocol -Uri $ProtocolUri
    Start-Sleep -Seconds 3

    $warm = @(Get-XueqingProcesses)
    $warmIds = @($warm | Select-Object -ExpandProperty Id)
    if ($warm.Count -lt 1) {
        throw 'Warm protocol activation unexpectedly left no Xueqing process.'
    }

    $diagnostic = [ordered]@{
        protocolUri = $ProtocolUri
        authBoundaryObserved = $true
        authProcessId = $auth.ProcessId
        coldProcessCount = $cold.Count
        coldProcessIds = $coldIds
        warmProcessCount = $warm.Count
        warmProcessIds = $warmIds
        warmActivationCreatedAdditionalInstance = ($warm.Count -gt $cold.Count)
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }

    $json = $diagnostic | ConvertTo-Json -Depth 4
    Write-Host $json

    if ($DiagnosticOutput) {
        Set-Content -Path $DiagnosticOutput -Value $json -Encoding utf8NoBOM
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        @"
## Windows packaged protocol activation

- URI: $ProtocolUri
- Installed package cold activation reached normal Auth UI: yes
- Cold process count: $($cold.Count)
- Warm process count after second protocol activation: $($warm.Count)
- Warm activation created an additional process: $($warm.Count -gt $cold.Count)
"@ | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}
finally {
    Stop-XueqingProcesses
}
