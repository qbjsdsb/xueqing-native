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

function Find-ListItems {
    param([Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$List)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return $List.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
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
Write-Host "[ux-smoke] Native process exposed a main window."

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
Write-Host "[ux-smoke] Pressure window: ${targetWidthDips}x${targetHeightDips} DIP at ${dpi} DPI (${targetWidthPixels}x${targetHeightPixels} px)."
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
Write-Host '[ux-smoke] Navigated to Students.'

$searchBox = Wait-Until -FailureMessage 'Student search box did not appear after navigating to Students.' -Condition {
    Find-ByAutomationId -Root $root -AutomationId 'StudentSearchBox'
}

$valuePattern = $null
if (-not $searchBox.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
    throw 'Student search box does not expose ValuePattern.'
}
([System.Windows.Automation.ValuePattern]$valuePattern).SetValue('S000777')

Wait-Until -FailureMessage 'Student search box did not retain the requested unique student code.' -Condition {
    $liveSearch = Find-ByAutomationId -Root $root -AutomationId 'StudentSearchBox'
    if ($null -eq $liveSearch) {
        return $false
    }
    $liveValue = $null
    if (-not $liveSearch.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$liveValue)) {
        return $false
    }
    return ([System.Windows.Automation.ValuePattern]$liveValue).Current.Value -eq 'S000777'
} | Out-Null
Write-Host '[ux-smoke] Unique student-code search accepted.'

# The query S000777 is unique in the deterministic 1,000-student fixture. Do
# not depend on TextBlock UIA Name values: WinUI virtualizes row descendants and
# those names are not a stable contract. Instead require the native ListView to
# converge to exactly one live ListItem after the unique query, then select it.
$studentItem = Wait-Until -FailureMessage 'Unique S000777 search did not converge to exactly one native ListItem.' -Condition {
    $liveList = Find-ByAutomationId -Root $root -AutomationId 'StudentList'
    if ($null -eq $liveList) {
        return $null
    }
    $items = Find-ListItems -List $liveList
    if ($items.Count -ne 1) {
        return $null
    }
    return $items[0]
}
Write-Host '[ux-smoke] Filtered StudentList exposes exactly one native ListItem.'
Invoke-Element -Element $studentItem

$backButton = Wait-Until -FailureMessage 'Compact Student Detail did not expose the return action after selecting the filtered student.' -Condition {
    Find-ByAutomationId -Root $root -AutomationId 'BackToStudentList'
}
Write-Host '[ux-smoke] Compact Student Detail opened.'
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
Write-Host '[ux-smoke] Search context survived detail return.'

# Focus changes are asynchronous across the WinUI/UIA boundary. Re-acquire both
# the focused element and the live list until the programmatic focus restoration
# becomes observable instead of sampling once and creating a timing-only failure.
Wait-Until -FailureMessage 'Focus did not return to the Student list after detail close.' -Condition {
    $liveList = Find-ByAutomationId -Root $root -AutomationId 'StudentList'
    if ($null -eq $liveList) {
        return $false
    }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused) {
        return $false
    }
    return Is-DescendantOf -Element $focused -Ancestor $liveList
} | Out-Null

Write-Host 'Windows native UX smoke passed: 800 DIP compact navigation, unique search, detail, context and focus restoration.'
