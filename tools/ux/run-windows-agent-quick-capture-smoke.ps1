[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$ProcessName = 'Xueqing.Windows',
    [string]$DiagnosticOutput = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Condition,
        [int]$TimeoutSeconds = 20,
        [string]$FailureMessage = 'Condition was not satisfied before timeout.'
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $result = & $Condition
            if ($result) {
                return $result
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
        catch [System.InvalidOperationException] {
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw $FailureMessage
}

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)][string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-ListItems {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$List)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return $List.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Invoke-Element {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        return
    }

    if ($Element.TryGetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }

    throw "Element '$($Element.Current.AutomationId)' is not invokable/selectable."
}

function Get-ValuePattern {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern,
        [ref]$pattern)) {
        throw "Element '$($Element.Current.AutomationId)' does not expose ValuePattern."
    }

    return [System.Windows.Automation.ValuePattern]$pattern
}

function Wait-For-AppExit {
    param([int]$TimeoutSeconds = 10)

    Wait-Until -TimeoutSeconds $TimeoutSeconds -FailureMessage "Process '$ProcessName' did not fully exit." -Condition {
        return @(
            Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
        ).Count -eq 0
    } | Out-Null
}

function Attach-To-App {
    $process = Wait-Until -TimeoutSeconds 30 -FailureMessage "Process '$ProcessName' did not expose a native window." -Condition {
        Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
            Select-Object -First 1
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    if ($null -eq $root) {
        throw 'UI Automation could not obtain the WinUI root element.'
    }

    return [pscustomobject]@{
        Process = $process
        Root = $root
    }
}

function Navigate-ToStudents {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root)

    $navigation = Wait-Until -FailureMessage 'Students navigation is unavailable.' -Condition {
        Find-ByAutomationId -Root $Root -AutomationId 'StudentsNavigation'
    }
    Invoke-Element -Element $navigation

    Wait-Until -FailureMessage 'Students surface did not load.' -Condition {
        Find-ByAutomationId -Root $Root -AutomationId 'StudentsSurface'
    } | Out-Null
}

function Ensure-StudentContextLoaded {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root)

    $draft = Wait-Until -TimeoutSeconds 8 -FailureMessage 'Observation draft editor did not become available from the authoritative Student context.' -Condition {
        $candidate = Find-ByAutomationId -Root $Root -AutomationId 'ObservationDraftText'
        if ($null -ne $candidate -and $candidate.Current.IsEnabled) {
            return $candidate
        }

        $list = Find-ByAutomationId -Root $Root -AutomationId 'StudentList'
        if ($null -eq $list) {
            return $null
        }

        $items = @(Find-ListItems -List $list)
        if ($items.Count -eq 0) {
            return $null
        }

        try {
            Invoke-Element -Element $items[0]
        }
        catch [System.Management.Automation.MethodInvocationException] {
        }
        return $null
    }

    return $draft
}

function Start-Xueqing {
    if (-not (Test-Path $ExecutablePath)) {
        throw "Installed Xueqing executable does not exist: $ExecutablePath"
    }

    Start-Process -FilePath $ExecutablePath | Out-Null
    return Attach-To-App
}

function Stop-Xueqing {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-For-AppExit
}

function Read-CreateObservationCallCount {
    param([Parameter(Mandatory = $true)][string]$StatePath)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        if (Test-Path $StatePath) {
            $value = (Get-Content $StatePath -Raw).Trim()
            if ($value -match '^\d+$') {
                return [int]$value
            }
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw 'Loopback provider call counter is unavailable.'
}

function Start-LoopbackProvider {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$StatePath
    )

    $job = Start-Job -ArgumentList $Port, $StatePath -ScriptBlock {
        param($Port, $StatePath)

        $ErrorActionPreference = 'Stop'
        Set-Content -Path $StatePath -Value '0' -Encoding ascii

        $actor = '10000000-0000-0000-0000-000000000001'
        $organization = '20000000-0000-0000-0000-000000000001'
        $student = '30000000-0000-0000-0000-000000000001'
        $subjectProfile = '40000000-0000-0000-0000-000000000001'
        $assignment = '50000000-0000-0000-0000-000000000001'
        $generated = '2026-09-22T12:00:00Z'

        $bootstrap = @{
            contract = 'personal_bootstrap_v1'
            generated_at_server = $generated
            actor = @{
                app_user_id = $actor
                display_name = '虚构教师'
            }
            organizations = @(
                @{
                    organization_id = $organization
                    name = '虚构学情机构'
                    can_teach = $true
                }
            )
            teaching_contexts = @(
                @{
                    organization_id = $organization
                    student_id = $student
                    student_display_name = '虚构学生0001'
                    subject_profile_id = $subjectProfile
                    subject_key = 'chinese'
                    assignment_id = $assignment
                }
            )
        } | ConvertTo-Json -Depth 8 -Compress

        $today = @{
            contract = 'personal_today_actions_v1'
            generated_at_server = $generated
            actor_app_user_id = $actor
            actions = @()
            has_more = $false
        } | ConvertTo-Json -Depth 6 -Compress

        $recent = @{
            contract = 'student_recent_observations_v1'
            generated_at_server = $generated
            actor_app_user_id = $actor
            organization_id = $organization
            student_id = $student
            student_display_name = '虚构学生0001'
            subject_profile_id = $subjectProfile
            subject_key = 'chinese'
            assignment_id = $assignment
            observations = @()
            has_more = $false
        } | ConvertTo-Json -Depth 6 -Compress

        $focus = @{
            contract = 'student_learning_focus_v1'
            generated_at_server = $generated
            actor_app_user_id = $actor
            organization_id = $organization
            organization_name = '虚构学情机构'
            organization_time_zone = 'Asia/Shanghai'
            organization_business_date = '2026-09-22'
            student_id = $student
            student_display_name = '虚构学生0001'
            subject_profile_id = $subjectProfile
            subject_key = 'chinese'
            assignment_id = $assignment
            cases = @()
            has_more = $false
        } | ConvertTo-Json -Depth 8 -Compress

        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://127.0.0.1:$Port/")
        $listener.Start()
        try {
            while ($listener.IsListening) {
                $context = $listener.GetContext()
                $path = $context.Request.Url.AbsolutePath
                $status = 200
                $body = switch ($path) {
                    '/health' {
                        '{"ok":true}'
                        break
                    }
                    '/rest/v1/rpc/get_personal_bootstrap_v1' {
                        $bootstrap
                        break
                    }
                    '/rest/v1/rpc/get_personal_today_actions_v1' {
                        $today
                        break
                    }
                    '/rest/v1/rpc/get_student_recent_observations_v1' {
                        $recent
                        break
                    }
                    '/rest/v1/rpc/get_student_learning_focus_v1' {
                        $focus
                        break
                    }
                    '/rest/v1/rpc/get_organization_management_v1' {
                        $status = 400
                        '{"message":"XQ_ORGANIZATION_MANAGEMENT_REQUIRED"}'
                        break
                    }
                    '/rest/v1/rpc/create_observation' {
                        $count = [int](Get-Content $StatePath -Raw)
                        Set-Content -Path $StatePath -Value ($count + 1) -Encoding ascii
                        $status = 409
                        '{"message":"XQ_AGENT_ACCEPTANCE_MUST_NOT_SUBMIT"}'
                        break
                    }
                    default {
                        $status = 404
                        '{"message":"XQ_TEST_ROUTE_NOT_FOUND"}'
                    }
                }

                $bytes = [Text.Encoding]::UTF8.GetBytes($body)
                $context.Response.StatusCode = $status
                $context.Response.ContentType = 'application/json; charset=utf-8'
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                $context.Response.OutputStream.Close()
            }
        }
        finally {
            $listener.Stop()
            $listener.Close()
        }
    }

    return $job
}

$diagnosticDirectory = if ([string]::IsNullOrWhiteSpace($DiagnosticOutput)) {
    ''
}
else {
    Split-Path -Parent $DiagnosticOutput
}
if (-not [string]::IsNullOrWhiteSpace($diagnosticDirectory)) {
    New-Item -ItemType Directory -Force -Path $diagnosticDirectory | Out-Null
}

$statePath = Join-Path ([IO.Path]::GetTempPath()) "xueqing-agent-quick-capture-$([Guid]::NewGuid().ToString('N')).txt"
$tcp = [System.Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$tcp.Start()
$port = ([Net.IPEndPoint]$tcp.LocalEndpoint).Port
$tcp.Stop()

$providerJob = $null
$draftText = "Agent 草稿恢复验证 $([Guid]::NewGuid().ToString('N'))"
$firstProcessId = $null
$secondProcessId = $null

try {
    Stop-Xueqing

    $providerJob = Start-LoopbackProvider -Port $port -StatePath $statePath
    Wait-Until -TimeoutSeconds 10 -FailureMessage 'Loopback reference provider did not become ready.' -Condition {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/health" -TimeoutSec 1
            return $response.StatusCode -eq 200
        }
        catch {
            return $false
        }
    } | Out-Null

    $env:XUEQING_LOCAL_REFERENCE_PROVIDER_URL = "http://127.0.0.1:$port/"
    $env:XUEQING_LOCAL_REFERENCE_API_KEY = 'local-agent-acceptance-key'
    $env:XUEQING_LOCAL_REFERENCE_ACCESS_TOKEN = 'local-agent-acceptance-token'

    $first = Start-Xueqing
    $firstProcessId = $first.Process.Id
    Navigate-ToStudents -Root $first.Root
    $draft = Ensure-StudentContextLoaded -Root $first.Root
    $value = Get-ValuePattern -Element $draft
    $value.SetValue($draftText)

    Wait-Until -TimeoutSeconds 15 -FailureMessage 'Quick Capture did not expose the consequential submit boundary after draft input.' -Condition {
        $button = Find-ByAutomationId -Root $first.Root -AutomationId 'ObservationSubmit'
        return $null -ne $button -and $button.Current.IsEnabled
    } | Out-Null

    # The acceptance journey intentionally stops here. Never invoke ObservationSubmit.
    Start-Sleep -Milliseconds 800
    if ((Read-CreateObservationCallCount -StatePath $statePath) -ne 0) {
        throw 'Agent draft journey crossed the consequential write boundary before authorization.'
    }

    Stop-Xueqing

    $second = Start-Xueqing
    $secondProcessId = $second.Process.Id
    Navigate-ToStudents -Root $second.Root
    $recoveredDraft = Ensure-StudentContextLoaded -Root $second.Root
    $recoveredValue = Get-ValuePattern -Element $recoveredDraft

    Wait-Until -TimeoutSeconds 15 -FailureMessage 'Durable Quick Capture draft was not recovered after forced process restart.' -Condition {
        return ([System.Windows.Automation.ValuePattern]$recoveredValue).Current.Value -eq $draftText
    } | Out-Null

    $calls = Read-CreateObservationCallCount -StatePath $statePath
    if ($calls -ne 0) {
        throw "Agent recovery journey invoked create_observation $calls time(s) without authorization."
    }

    $result = [ordered]@{
        schemaVersion = 1
        status = 'passed'
        provider = "http://127.0.0.1:$port/"
        firstProcessId = $firstProcessId
        secondProcessId = $secondProcessId
        draftRecoveredExactly = $true
        createObservationCalls = $calls
        submitInvoked = $false
    }

    if (-not [string]::IsNullOrWhiteSpace($DiagnosticOutput)) {
        $result | ConvertTo-Json -Depth 4 |
            Set-Content -Path $DiagnosticOutput -Encoding utf8
    }

    Write-Host '[agent-quick-capture] Exact packaged UIA draft recovery passed; create_observation_calls=0.'
}
finally {
    Stop-Xueqing

    Remove-Item Env:\XUEQING_LOCAL_REFERENCE_PROVIDER_URL -ErrorAction SilentlyContinue
    Remove-Item Env:\XUEQING_LOCAL_REFERENCE_API_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:\XUEQING_LOCAL_REFERENCE_ACCESS_TOKEN -ErrorAction SilentlyContinue

    if ($null -ne $providerJob) {
        Stop-Job $providerJob -ErrorAction SilentlyContinue
        Remove-Job $providerJob -Force -ErrorAction SilentlyContinue
    }

    Remove-Item $statePath -Force -ErrorAction SilentlyContinue
}
