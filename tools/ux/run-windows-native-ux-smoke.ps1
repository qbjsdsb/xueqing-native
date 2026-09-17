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

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);
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
        try {
            $result = & $Condition
            if ($result) {
                return $result
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            # WinUI can briefly replace virtualized elements while a filtered
            # ItemsSource is changing. Re-acquire them on the next iteration.
        }
        catch [System.InvalidOperationException] {
            # Some providers surface transient COM/UIA invalid-operation errors
            # during reparenting. A later query against the live tree is valid.
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

function Find-FirstListItem {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$List)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return $List.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Get-AccessibleText {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root)

    $parts = New-Object System.Collections.Generic.List[string]
    try {
        if (-not [string]::IsNullOrWhiteSpace($Root.Current.Name)) {
            $parts.Add($Root.Current.Name)
        }
    }
    catch [System.Windows.Automation.ElementNotAvailableException] {
        return ''
    }

    $all = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        try {
            $name = $element.Current.Name
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $parts.Add($name)
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            continue
        }
    }
    return ($parts -join ' | ')
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
        try {
            $current = $walker.GetParent($current)
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            return $false
        }
    }
    return $false
}

$process = Wait-Until -FailureMessage "Process '$ProcessName' did not expose a native main window." -Condition {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Select-Object -First 1
}

$windowHandle = $process.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
if ($null -eq $root) {
    throw 'UI Automation could not obtain the WinUI root element.'
}

# SetWindowPos consumes physical pixels, while the UX acceptance matrix is in
# device-independent pixels. Convert the required 800 x 640 DIP pressure case
# using the actual DPI of the native window instead of assuming 100% scaling.
$dpi = [XueqingWindowNativeMethods]::GetDpiForWindow($windowHandle)
if ($dpi -eq 0) {
    throw 'GetDpiForWindow returned 0; the 800 DIP validation would be ambiguous.'
}
$targetWidthDips = 800
$targetHeightDips = 640
$targetWidthPixels = [int][Math]::Round($targetWidthDips * $dpi / 96.0)
$targetHeightPixels = [int][Math]::Round($targetHeightDips * $dpi / 96.0)
$SWP_NOMOVE = 0x0002
if (-not [XueqingWindowNativeMethods]::SetWindowPos(
    $windowHandle,
    [IntPtr]::Zero,
    0,
    0,
    $targetWidthPixels,
    $targetHeightPixels,
    $SWP_NOMOVE)) {
    throw "SetWindowPos failed with Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
}
Write-Host "Native UX pressure window: ${targetWidthDips}x${targetHeightDips} DIP at ${dpi} DPI (${targetWidthPixels}x${targetHeightPixels} px)."
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

# Do not assume a TextBlock has a stable UIA Name. WinUI virtualizes and
# replaces row descendants while filtering. Re-acquire the ListView and select
# the one live ListItem that remains after the unique student-code search.
$studentItem = Wait-Until -FailureMessage 'Filtered student S000777 did not materialize as a native ListItem.' -Condition {
    $liveList = Find-ByAutomationId -Root $root -AutomationId 'StudentList'
    if ($null -eq $liveList) {
        return $null
    }
    $item = Find-FirstListItem -List $liveList
    if ($null -eq $item) {
        return $null
    }
    $accessibleText = Get-AccessibleText -Root $item
    if ($accessibleText -notlike '*S000777*') {
        return $null
    }
    Write-Host "Filtered native student row: $accessibleText"
    return $item
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
if ($null -eq $focused) {
    throw 'No focused native element was reported after returning from Student Detail.'
}
if (-not (Is-DescendantOf -Element $focused -Ancestor $studentList)) {
    throw "Focus did not return to the Student list after detail close. Focused='$($focused.Current.AutomationId)'."
}

Write-Host 'Windows native UX smoke passed: 800 DIP compact navigation, search, detail, context and focus restoration.'
