[CmdletBinding()]
param(
    [string]$ProcessName = 'Xueqing.Windows'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class XueqingWindowNativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
'@

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Condition,
        [int]$TimeoutSeconds = 20,
        [string]$FailureMessage = 'Condition was not satisfied before timeout.'
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $result = & $Condition
        if ($result) {
            return $result
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
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ByName {
    param(
        [Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Element {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        return
    }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }

    throw "Element '$($Element.Current.AutomationId)' exposes neither SelectionItemPattern nor InvokePattern."
}

function Is-DescendantOf {
    param(
        [Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Element,
        [Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Ancestor
    )

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $current = $Element
    while ($null -ne $current) {
        if ($current -eq $Ancestor) {
            return $true
        }
        $current = $walker.GetParent($current)
    }
    return $false
}

$process = Wait-Until -FailureMessage "Process '$ProcessName' did not expose a native main window." -Condition {
    $candidate = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Select-Object -First 1
    return $candidate
}

$windowHandle = $process.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
if ($null -eq $root) {
    throw 'UI Automation could not obtain the WinUI root element.'
}

# Exercise the acceptance gate at the narrowest required width. Height remains
# intentionally short enough to pressure vertical layout without clipping the app.
$SWP_NOMOVE = 0x0002
if (-not [XueqingWindowNativeMethods]::SetWindowPos($windowHandle, [IntPtr]::Zero, 0, 0, 800, 640, $SWP_NOMOVE)) {
    throw "SetWindowPos failed with Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
}
Start-Sleep -Milliseconds 500

$today = Find-ByAutomationId -Root $root -AutomationId 'TodayNavigation'
if ($null -eq $today) {
    throw 'Today navigation item is not exposed to UI Automation.'
}

$studentsNav = Find-ByAutomationId -Root $root -AutomationId 'StudentsNavigation'
if ($null -eq $studentsNav) {
    throw 'Students navigation item is not exposed to UI Automation.'
}
Invoke-Element -Element $studentsNav

$searchBox = Wait-Until -FailureMessage 'Student search box did not appear after navigating to Students.' -Condition {
    Find-ByAutomationId -Root $root -AutomationId 'StudentSearchBox'
}

$valuePattern = $null
if (-not $searchBox.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
    throw 'Student search box does not expose ValuePattern.'
}
([System.Windows.Automation.ValuePattern]$valuePattern).SetValue('S000777')

$studentText = Wait-Until -FailureMessage 'Filtered student S000777 was not rendered.' -Condition {
    Find-ByName -Root $root -Name '虚构学生0777'
}

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$studentItem = $studentText
while ($null -ne $studentItem -and $studentItem.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
    $studentItem = $walker.GetParent($studentItem)
}
if ($null -eq $studentItem) {
    throw 'Could not resolve the filtered student to a native ListItem.'
}
Invoke-Element -Element $studentItem

$backButton = Wait-Until -FailureMessage 'Compact Student Detail did not expose the return action.' -Condition {
    Find-ByAutomationId -Root $root -AutomationId 'BackToStudentList'
}
Invoke-Element -Element $backButton

$searchBoxAfterReturn = Wait-Until -FailureMessage 'Student list was not restored after returning from detail.' -Condition {
    Find-ByAutomationId -Root $root -AutomationId 'StudentSearchBox'
}
$valueAfterReturn = $null
if (-not $searchBoxAfterReturn.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valueAfterReturn)) {
    throw 'Restored search box does not expose ValuePattern.'
}
if (([System.Windows.Automation.ValuePattern]$valueAfterReturn).Current.Value -ne 'S000777') {
    throw 'Returning from Student Detail lost the search context.'
}

$studentList = Find-ByAutomationId -Root $root -AutomationId 'StudentList'
if ($null -eq $studentList) {
    throw 'Restored Student list is not exposed to UI Automation.'
}
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
if ($null -eq $focused -or -not (Is-DescendantOf -Element $focused -Ancestor $studentList)) {
    throw "Focus did not return to the Student list after detail close. Focused='$($focused.Current.AutomationId)'."
}

Write-Host 'Windows native UX smoke passed: 800px compact navigation, search, detail, context and focus restoration.'
