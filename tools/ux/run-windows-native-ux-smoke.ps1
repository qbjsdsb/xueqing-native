[CmdletBinding()]
param(
    [string]$ProcessName = 'Xueqing.Windows',
    [string]$EvidenceDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing.Common

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class XueqingWindowNativeMethods
{
    public const uint SPI_GETHIGHCONTRAST = 0x0042;
    public const uint SPI_SETHIGHCONTRAST = 0x0043;
    public const uint HCF_HIGHCONTRASTON = 0x00000001;
    public const uint SPIF_UPDATEINIFILE = 0x0001;
    public const uint SPIF_SENDCHANGE = 0x0002;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        ref HIGHCONTRAST pvParam,
        uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    public static Tuple<int, int> GetClientSize(IntPtr hWnd)
    {
        RECT rect;
        if (!GetClientRect(hWnd, out rect))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return Tuple.Create(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static Tuple<uint, string> GetHighContrastState()
    {
        var state = new HIGHCONTRAST();
        state.cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>();
        if (!SystemParametersInfoW(SPI_GETHIGHCONTRAST, state.cbSize, ref state, 0))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        var scheme = state.lpszDefaultScheme == IntPtr.Zero
            ? String.Empty
            : Marshal.PtrToStringUni(state.lpszDefaultScheme) ?? String.Empty;
        return Tuple.Create(state.dwFlags, scheme);
    }

    public static void SetHighContrastState(uint flags, string scheme)
    {
        IntPtr schemePointer = IntPtr.Zero;
        try
        {
            if (!String.IsNullOrWhiteSpace(scheme))
            {
                schemePointer = Marshal.StringToHGlobalUni(scheme);
            }

            var state = new HIGHCONTRAST();
            state.cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>();
            state.dwFlags = flags;
            state.lpszDefaultScheme = schemePointer;
            if (!SystemParametersInfoW(
                SPI_SETHIGHCONTRAST,
                state.cbSize,
                ref state,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (schemePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(schemePointer);
            }
        }
    }

    public static void BroadcastSettingChange(string area)
    {
        IntPtr areaPointer = IntPtr.Zero;
        try
        {
            areaPointer = Marshal.StringToHGlobalUni(area ?? String.Empty);
            IntPtr result;
            SendMessageTimeoutW(
                new IntPtr(0xffff),
                WM_SETTINGCHANGE,
                IntPtr.Zero,
                areaPointer,
                SMTO_ABORTIFHUNG,
                5000,
                out result);
        }
        finally
        {
            if (areaPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(areaPointer);
            }
        }
    }
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
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-VisibleByAutomationId {
    param(
        [Parameter(Mandatory = $true)][System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)][string]$AutomationId
    )

    $element = Find-ByAutomationId -Root $Root -AutomationId $AutomationId
    if ($null -eq $element) {
        return $null
    }

    $rectangle = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $rectangle.Width -le 0 -or $rectangle.Height -le 0) {
        return $null
    }
    return $element
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

function Get-RegistryValueSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $pathExists = Test-Path $Path
    if (-not $pathExists) {
        return [pscustomobject]@{ PathExisted = $false; ValueExisted = $false; Value = $null }
    }

    $item = Get-ItemProperty -Path $Path -ErrorAction Stop
    $property = $item.PSObject.Properties[$Name]
    return [pscustomobject]@{
        PathExisted = $true
        ValueExisted = $null -ne $property
        Value = if ($null -ne $property) { $property.Value } else { $null }
    }
}

function Restore-RegistryValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Snapshot
    )

    if ($Snapshot.ValueExisted) {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
        }
        New-ItemProperty -Path $Path -Name $Name -Value ([int]$Snapshot.Value) -PropertyType DWord -Force | Out-Null
    }
    elseif (Test-Path $Path) {
        Remove-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        if (-not $Snapshot.PathExisted) {
            $remaining = Get-ItemProperty -Path $Path -ErrorAction SilentlyContinue
            if ($null -ne $remaining) {
                # Do not delete a shared Windows settings key merely because this
                # single value did not exist before the isolated test.
            }
        }
    }
}

function Set-RegistryDword {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Value,
        [Parameter(Mandatory = $true)][string]$BroadcastArea
    )

    if (-not (Test-Path $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
    [XueqingWindowNativeMethods]::BroadcastSettingChange($BroadcastArea)
}

$script:process = $null
$script:root = $null
$script:windowHandle = [IntPtr]::Zero
$script:dpi = 0
$script:executablePath = $null

function Attach-To-App {
    $script:process = Wait-Until -FailureMessage "Process '$ProcessName' did not expose a native main window." -Condition {
        Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
            Select-Object -First 1
    }
    $script:windowHandle = $script:process.MainWindowHandle
    $script:root = [System.Windows.Automation.AutomationElement]::FromHandle($script:windowHandle)
    if ($null -eq $script:root) {
        throw 'UI Automation could not obtain the WinUI root element.'
    }
    $script:dpi = [XueqingWindowNativeMethods]::GetDpiForWindow($script:windowHandle)
    if ($script:dpi -eq 0) {
        throw 'GetDpiForWindow returned 0; DIP validation would be ambiguous.'
    }
    if ([string]::IsNullOrWhiteSpace($script:executablePath)) {
        $script:executablePath = $script:process.Path
    }
}

function Wait-For-AppProcessesToExit {
    param([int]$TimeoutSeconds = 10)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(
            Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
        )
        if ($remaining.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $remainingIds = @(
        Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Id
    )
    throw "Process '$ProcessName' did not fully exit before restart. Remaining process ids: $($remainingIds -join ', ')."
}

function Restart-App {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Wait-For-AppProcessesToExit
    Start-Process -FilePath $script:executablePath | Out-Null
    Attach-To-App
}

function Set-WindowDips {
    param(
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height
    )

    $targetWidthPixels = [int][Math]::Round($Width * $script:dpi / 96.0)
    $targetHeightPixels = [int][Math]::Round($Height * $script:dpi / 96.0)
    $outerWidthPixels = $targetWidthPixels
    $outerHeightPixels = $targetHeightPixels
    $SWP_NOMOVE = 0x0002

    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        if (-not [XueqingWindowNativeMethods]::SetWindowPos(
            $script:windowHandle,
            [IntPtr]::Zero,
            0,
            0,
            $outerWidthPixels,
            $outerHeightPixels,
            $SWP_NOMOVE)) {
            throw "SetWindowPos failed with Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
        }

        Start-Sleep -Milliseconds 150
        $clientSize = [XueqingWindowNativeMethods]::GetClientSize($script:windowHandle)
        $widthDelta = $targetWidthPixels - $clientSize.Item1
        $heightDelta = $targetHeightPixels - $clientSize.Item2
        if ([Math]::Abs($widthDelta) -le 1 -and [Math]::Abs($heightDelta) -le 1) {
            break
        }

        $outerWidthPixels += $widthDelta
        $outerHeightPixels += $heightDelta
    }

    $clientSize = [XueqingWindowNativeMethods]::GetClientSize($script:windowHandle)
    if ([Math]::Abs($clientSize.Item1 - $targetWidthPixels) -gt 1 -or
        [Math]::Abs($clientSize.Item2 - $targetHeightPixels) -gt 1) {
        throw "Client area did not converge to ${Width}x${Height} DIP: expected ${targetWidthPixels}x${targetHeightPixels} px, got $($clientSize.Item1)x$($clientSize.Item2) px."
    }

    Write-Host "[ux-matrix] Client ${Width}x${Height} DIP at $($script:dpi) DPI ($($clientSize.Item1)x$($clientSize.Item2) px client; ${outerWidthPixels}x${outerHeightPixels} px outer)."
    Start-Sleep -Milliseconds 300
}

function Save-WindowScreenshot {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
    $rectangle = $script:root.Current.BoundingRectangle
    $x = [int][Math]::Floor($rectangle.X)
    $y = [int][Math]::Floor($rectangle.Y)
    $width = [int][Math]::Ceiling($rectangle.Width)
    $height = [int][Math]::Ceiling($rectangle.Height)
    if ($width -le 0 -or $height -le 0) {
        throw "Cannot capture '$Name': native window has an empty bounding rectangle."
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($x, $y, 0, 0, $bitmap.Size)
        $path = Join-Path $EvidenceDirectory "$Name.png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "[ux-evidence] Saved representative screenshot: $path"
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Navigate-ToSurface {
    param(
        [Parameter(Mandatory = $true)][string]$NavigationId,
        [Parameter(Mandatory = $true)][string]$SurfaceId
    )

    $navigation = Wait-Until -FailureMessage "Navigation '$NavigationId' is unavailable." -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId $NavigationId
    }
    Invoke-Element -Element $navigation

    Wait-Until -FailureMessage "Navigation '$NavigationId' was invoked but never became selected." -Condition {
        $liveNavigation = Find-VisibleByAutomationId -Root $script:root -AutomationId $NavigationId
        if ($null -eq $liveNavigation) {
            return $false
        }

        $selectionPattern = $null
        if (-not $liveNavigation.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref]$selectionPattern)) {
            return $false
        }

        return ([System.Windows.Automation.SelectionItemPattern]$selectionPattern).Current.IsSelected
    } | Out-Null

    $surface = Wait-Until -FailureMessage "Surface '$SurfaceId' did not become visible." -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId $SurfaceId
    }

    # UIA selection changes can become observable before WinUI has submitted the
    # corresponding frame to the compositor. Give the actual native surface a
    # bounded settle interval before screenshot evidence is captured.
    Start-Sleep -Milliseconds 300
    return $surface
}

function Switch-ToWorkspace {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('personal', 'organization')]
        [string]$Workspace
    )

    $switcher = Wait-Until -FailureMessage 'Workspace switcher is unavailable.' -Condition {
        Find-ByAutomationId -Root $script:root -AutomationId 'WorkspaceSwitcher'
    }
    Invoke-Element -Element $switcher

    $menuId = if ($Workspace -eq 'organization') {
        'OrganizationWorkspaceMenuItem'
    }
    else {
        'PersonalWorkspaceMenuItem'
    }

    $menuItem = Wait-Until -FailureMessage "Workspace menu item '$menuId' is unavailable." -Condition {
        Find-VisibleByAutomationId -Root ([System.Windows.Automation.AutomationElement]::RootElement) -AutomationId $menuId
    }
    Invoke-Element -Element $menuItem
    Start-Sleep -Milliseconds 200

    if ($Workspace -eq 'organization') {
        Wait-Until -FailureMessage 'Organization workspace navigation did not become visible.' -Condition {
            Find-VisibleByAutomationId -Root $script:root -AutomationId 'OrganizationManagementNavigation'
        } | Out-Null
    }
    else {
        Wait-Until -FailureMessage 'Personal teaching navigation did not become visible.' -Condition {
            Find-VisibleByAutomationId -Root $script:root -AutomationId 'TodayNavigation'
        } | Out-Null
    }
}

function Set-SearchValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

    $searchBox = Wait-Until -FailureMessage 'Student search box is unavailable.' -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentSearchBox'
    }
    $valuePattern = $null
    if (-not $searchBox.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
        throw 'Student search box does not expose ValuePattern.'
    }
    ([System.Windows.Automation.ValuePattern]$valuePattern).SetValue($Value)
}

function Get-UniqueFilteredStudentItem {
    return Wait-Until -FailureMessage 'Unique S000777 search did not converge to exactly one native ListItem.' -Condition {
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        if ($null -eq $liveList) {
            return $null
        }
        $items = @(Find-ListItems -List $liveList)
        if ($items.Count -ne 1) {
            return $null
        }
        return $items[0]
    }
}

function Assert-ChineseUnicodeSearch {
    Set-WindowDips -Width 800 -Height 640
    Switch-ToWorkspace -Workspace personal
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
    Set-SearchValue -Value '虚构学生0777'

    $item = Wait-Until -FailureMessage 'Chinese Unicode search did not converge to one Student ListItem.' -Condition {
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        if ($null -eq $liveList) {
            return $null
        }
        $items = @(Find-ListItems -List $liveList)
        if ($items.Count -ne 1) {
            return $null
        }
        return $items[0]
    }
    if ($item.Current.Name -notmatch '0777') {
        throw "Chinese Unicode search returned an unexpected Student item: '$($item.Current.Name)'."
    }

    Set-SearchValue -Value ''
    Wait-Until -FailureMessage 'Student list did not repopulate after clearing Chinese Unicode search.' -Condition {
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        if ($null -eq $liveList) {
            return $false
        }
        return @(Find-ListItems -List $liveList).Count -gt 1
    } | Out-Null
    Write-Host '[ux-matrix] Chinese Unicode search input passed through native UIA ValuePattern; real IME composition remains an explicit manual/device evidence gap.'
}

function Assert-CompactStudentKeyboardJourney {
    Set-WindowDips -Width 800 -Height 640
    Switch-ToWorkspace -Workspace personal
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null

    $focusableStudentItem = Wait-Until -FailureMessage 'Student list did not expose a focusable row before Ctrl+F accelerator check.' -Condition {
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        if ($null -eq $liveList) {
            return $null
        }

        $items = @(Find-ListItems -List $liveList)
        if ($items.Count -eq 0) {
            return $null
        }

        try {
            $items[0].SetFocus()
            return $items[0]
        }
        catch [System.Management.Automation.MethodInvocationException] {
            return $null
        }
        catch [System.InvalidOperationException] {
            return $null
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            return $null
        }
    }
    [System.Windows.Forms.SendKeys]::SendWait('^f')
    Wait-Until -FailureMessage 'Ctrl+F did not focus the Student search box.' -Condition {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        return $null -ne $focused -and $focused.Current.AutomationId -eq 'StudentSearchBox'
    } | Out-Null

    Set-SearchValue -Value 'S000777'
    $studentItem = Get-UniqueFilteredStudentItem
    Invoke-Element -Element $studentItem
    $studentItem.SetFocus()

    # Selection alone must not navigate in compact/standard mode. Teachers need
    # to move rapidly through a large list with the arrow keys before deciding
    # to open the selected Student.
    Start-Sleep -Milliseconds 250
    if ($null -ne (Find-VisibleByAutomationId -Root $script:root -AutomationId 'BackToStudentList')) {
        throw 'Selecting a Student navigated to detail before explicit activation.'
    }

    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Wait-Until -FailureMessage 'Enter did not open compact Student Detail.' -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId 'BackToStudentList'
    } | Out-Null

    [System.Windows.Forms.SendKeys]::SendWait('%{LEFT}')

    $searchBoxAfterReturn = Wait-Until -FailureMessage 'Alt+Left did not restore the Student list from compact detail.' -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentSearchBox'
    }
    $valueAfterReturn = $null
    if (-not $searchBoxAfterReturn.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valueAfterReturn)) {
        throw 'Restored search box does not expose ValuePattern.'
    }
    if (([System.Windows.Automation.ValuePattern]$valueAfterReturn).Current.Value -ne 'S000777') {
        throw 'Returning from Student Detail lost the search context.'
    }

    Wait-Until -FailureMessage 'Focus did not return to the Student list after detail close.' -Condition {
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        return $null -ne $liveList -and $null -ne $focused -and (Is-DescendantOf -Element $focused -Ancestor $liveList)
    } | Out-Null

    Set-SearchValue -Value ''
    Wait-Until -FailureMessage 'Student list did not repopulate after clearing search.' -Condition {
        Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
    } | Out-Null
    $firstVisible = Wait-Until -FailureMessage 'Student list did not expose a selectable native ListItem after clearing search.' -Condition {
        # Clearing the search rebuilds/virtualizes ListView items. Reacquire the
        # live List and first ListItem on every attempt so a transient stale UIA
        # SelectionItemPattern cannot fail an otherwise valid keyboard journey.
        $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
        if ($null -eq $liveList) {
            return $null
        }

        $items = @(Find-ListItems -List $liveList)
        if ($items.Count -eq 0) {
            return $null
        }

        try {
            Invoke-Element -Element $items[0]
            return $items[0]
        }
        catch [System.Management.Automation.MethodInvocationException] {
            # WinUI can transiently invalidate SelectionItemPattern while the
            # virtualized ListView is rebuilding after search changes. Retry
            # with a freshly acquired UIA element until the bounded timeout.
            return $null
        }
        catch [System.InvalidOperationException] {
            return $null
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            return $null
        }
    }
    $firstVisible.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}{DOWN}{DOWN}')
    Start-Sleep -Milliseconds 300
    if ($null -ne (Find-VisibleByAutomationId -Root $script:root -AutomationId 'BackToStudentList')) {
        throw 'Arrow-key Student browsing incorrectly navigated to detail.'
    }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    $liveList = Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList'
    if ($null -eq $focused -or $null -eq $liveList -or -not (Is-DescendantOf -Element $focused -Ancestor $liveList)) {
        throw 'Arrow-key Student browsing lost list focus.'
    }

    Write-Host '[ux-matrix] Compact keyboard journey passed: Ctrl+F focuses search; select/arrow browse stays in list; Enter opens detail; Alt+Left returns with search and focus preserved.'
}

function Assert-WidthMatrix {
    foreach ($width in @(800, 960, 1024, 1280, 1600)) {
        Set-WindowDips -Width $width -Height 640
        Switch-ToWorkspace -Workspace personal

        Navigate-ToSurface -NavigationId 'TodayNavigation' -SurfaceId 'TodaySurface' | Out-Null
        if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'TodayActionList')) {
            throw "Today Action list is unusable at ${width} DIP."
        }

        Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
        if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList')) {
            throw "Student list pane is unusable at ${width} DIP."
        }
        $detailVisible = $null -ne (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentDetailHeading')
        if ($width -ge 1280 -and -not $detailVisible) {
            throw "Expanded Student detail pane is missing at ${width} DIP."
        }
        if ($width -lt 1280 -and $detailVisible) {
            throw "Student list/detail was forced side-by-side at ${width} DIP."
        }

        Navigate-ToSurface -NavigationId 'LearningNavigation' -SurfaceId 'LearningSurface' | Out-Null
        if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'LearningTimeline')) {
            throw "Learning Case timeline is unusable at ${width} DIP."
        }

        Switch-ToWorkspace -Workspace organization
        Navigate-ToSurface -NavigationId 'OrganizationManagementNavigation' -SurfaceId 'OrganizationManagementSurface' | Out-Null
        $wideVisible = $null -ne (Find-VisibleByAutomationId -Root $script:root -AutomationId 'OrganizationWideList')
        $compactVisible = $null -ne (Find-VisibleByAutomationId -Root $script:root -AutomationId 'OrganizationCompactList')
        if ($width -ge 1280) {
            if (-not $wideVisible -or $compactVisible) {
                throw "Organization Management did not use the wide row model at ${width} DIP."
            }
        }
        elseif (-not $compactVisible -or $wideVisible) {
            throw "Organization Management did not degrade to compact rows at ${width} DIP."
        }
    }

    Set-WindowDips -Width 800 -Height 480
    Switch-ToWorkspace -Workspace personal
    Navigate-ToSurface -NavigationId 'TodayNavigation' -SurfaceId 'TodaySurface' | Out-Null
    if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'TodayActionList')) {
        throw 'Today Action list is unavailable at the 800x480 DIP short-window pressure case.'
    }
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
    if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentSearchBox') -or
        $null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList')) {
        throw 'Student search/list is unavailable at the 800x480 DIP short-window pressure case.'
    }

    Write-Host '[ux-matrix] Width matrix passed: 800/960/1024/1280/1600 DIP plus 800x480 short-window pressure.'
}

function Assert-RepresentativeSurfaces {
    Set-WindowDips -Width 1280 -Height 640
    Switch-ToWorkspace -Workspace personal
    Navigate-ToSurface -NavigationId 'TodayNavigation' -SurfaceId 'TodaySurface' | Out-Null
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
    Navigate-ToSurface -NavigationId 'LearningNavigation' -SurfaceId 'LearningSurface' | Out-Null
    Switch-ToWorkspace -Workspace organization
    Navigate-ToSurface -NavigationId 'OrganizationManagementNavigation' -SurfaceId 'OrganizationManagementSurface' | Out-Null
}

function Assert-DistinctScreenshotEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$LeftName,
        [Parameter(Mandatory = $true)][string]$RightName,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
        return
    }

    $leftPath = Join-Path $EvidenceDirectory "$LeftName.png"
    $rightPath = Join-Path $EvidenceDirectory "$RightName.png"
    if (-not (Test-Path $leftPath) -or -not (Test-Path $rightPath)) {
        throw "Representative evidence comparison is missing '$LeftName' or '$RightName'."
    }

    $leftHash = (Get-FileHash -Path $leftPath -Algorithm SHA256).Hash
    $rightHash = (Get-FileHash -Path $rightPath -Algorithm SHA256).Hash
    if ($leftHash -eq $rightHash) {
        throw "$FailureMessage Both screenshots have SHA-256 $leftHash."
    }
}

function Capture-BaseRepresentativeEvidence {
    Set-WindowDips -Width 800 -Height 640
    Switch-ToWorkspace -Workspace personal
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
    Save-WindowScreenshot -Name '800-compact-students'

    Set-WindowDips -Width 1280 -Height 640
    Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
    Save-WindowScreenshot -Name '1280-expanded-students'

    Navigate-ToSurface -NavigationId 'TodayNavigation' -SurfaceId 'TodaySurface' | Out-Null
    Save-WindowScreenshot -Name '1280-today'
    Assert-DistinctScreenshotEvidence -LeftName '1280-expanded-students' -RightName '1280-today' -FailureMessage 'Today evidence is identical to Students evidence; navigation/render evidence is stale.'

    Navigate-ToSurface -NavigationId 'LearningNavigation' -SurfaceId 'LearningSurface' | Out-Null
    Save-WindowScreenshot -Name '1280-learning'
    Assert-DistinctScreenshotEvidence -LeftName '1280-today' -RightName '1280-learning' -FailureMessage 'Learning evidence is identical to Today evidence; navigation/render evidence is stale.'
}

function Assert-NoHardCodedPrototypeColors {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $paths = @(
        (Join-Path $repositoryRoot 'apps/windows/src/Xueqing.Windows/MainWindow.xaml'),
        (Join-Path $repositoryRoot 'apps/windows/src/Xueqing.Windows/Views')
    )
    $files = @()
    foreach ($path in $paths) {
        if (Test-Path $path -PathType Leaf) {
            $files += Get-Item $path
        }
        elseif (Test-Path $path -PathType Container) {
            $files += Get-ChildItem $path -Recurse -File -Filter *.xaml
        }
    }

    $matches = $files | Select-String -Pattern '(Foreground|Background|BorderBrush)\s*=\s*"#[0-9A-Fa-f]{3,8}"'
    if ($matches) {
        $details = ($matches | ForEach-Object { "$($_.Path):$($_.LineNumber) $($_.Line.Trim())" }) -join [Environment]::NewLine
        throw "Prototype XAML hard-codes semantic colors that can defeat High Contrast:`n$details"
    }
    Write-Host '[ux-matrix] High Contrast static preflight passed: no hard-coded hex semantic brushes in prototype XAML.'
}

function Assert-TextScaleMatrix {
    param(
        [Parameter(Mandatory = $true)][string]$RegistryPath,
        [Parameter(Mandatory = $true)]$OriginalSnapshot
    )

    $heights = @{}
    foreach ($percent in @(100, 150, 200, 225)) {
        Set-RegistryDword -Path $RegistryPath -Name 'TextScaleFactor' -Value $percent -BroadcastArea 'Accessibility'
        Restart-App
        Set-WindowDips -Width 800 -Height 640
        Navigate-ToSurface -NavigationId 'StudentsNavigation' -SurfaceId 'StudentsSurface' | Out-Null
        $heading = Wait-Until -FailureMessage "Student heading is unavailable at ${percent}% text scale." -Condition {
            Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentHeading'
        }
        $height = $heading.Current.BoundingRectangle.Height
        if ($height -le 0) {
            throw "Student heading has no measurable height at ${percent}% text scale."
        }
        $heights[$percent] = $height
        if ($null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentSearchBox') -or
            $null -eq (Find-VisibleByAutomationId -Root $script:root -AutomationId 'StudentList')) {
            throw "Core Student task became unreachable at ${percent}% text scale."
        }
        if ($percent -eq 225) {
            Save-WindowScreenshot -Name '225-text-scale-students'
        }
        Write-Host "[ux-matrix] Text scale ${percent}% -> Student heading UIA height $height px at $($script:dpi) DPI."
    }

    if ($heights[150] -lt $heights[100] -or $heights[200] -lt $heights[150] -or $heights[225] -lt $heights[200]) {
        throw "Text-scale measurements were not monotonic: $($heights | Out-String)"
    }
    if ($heights[225] -lt ($heights[100] * 1.10)) {
        throw "225% system text scale did not measurably enlarge native WinUI text (100%=$($heights[100]), 225%=$($heights[225]))."
    }

    Assert-CompactStudentKeyboardJourney
    Write-Host '[ux-matrix] Text scale matrix passed at 100/150/200/225%; 225% retained core compact Student task completion.'
}

Attach-To-App
Write-Host '[ux-matrix] Native process exposed a main window.'

$accessibilityPath = 'HKCU:\Software\Microsoft\Accessibility'
$personalizePath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$textScaleSnapshot = Get-RegistryValueSnapshot -Path $accessibilityPath -Name 'TextScaleFactor'
$appsThemeSnapshot = Get-RegistryValueSnapshot -Path $personalizePath -Name 'AppsUseLightTheme'
$systemThemeSnapshot = Get-RegistryValueSnapshot -Path $personalizePath -Name 'SystemUsesLightTheme'
$highContrastSnapshot = [XueqingWindowNativeMethods]::GetHighContrastState()

try {
    Assert-NoHardCodedPrototypeColors
    Assert-WidthMatrix
    Capture-BaseRepresentativeEvidence
    Assert-ChineseUnicodeSearch
    Assert-CompactStudentKeyboardJourney

    foreach ($theme in @(
        @{ Name = 'Light'; Value = 1 },
        @{ Name = 'Dark'; Value = 0 }
    )) {
        Set-RegistryDword -Path $personalizePath -Name 'AppsUseLightTheme' -Value $theme.Value -BroadcastArea 'ImmersiveColorSet'
        Set-RegistryDword -Path $personalizePath -Name 'SystemUsesLightTheme' -Value $theme.Value -BroadcastArea 'ImmersiveColorSet'
        Restart-App
        Assert-RepresentativeSurfaces
        if ($theme.Name -eq 'Dark') {
            Save-WindowScreenshot -Name 'dark-1280-organization'
        }
        Write-Host "[ux-matrix] $($theme.Name) theme representative native surfaces passed."
    }

    $highContrastFlags = [uint32]($highContrastSnapshot.Item1 -bor [XueqingWindowNativeMethods]::HCF_HIGHCONTRASTON)
    $highContrastScheme = if ([string]::IsNullOrWhiteSpace($highContrastSnapshot.Item2)) { 'High Contrast Black' } else { $highContrastSnapshot.Item2 }
    [XueqingWindowNativeMethods]::SetHighContrastState($highContrastFlags, $highContrastScheme)
    Restart-App
    $liveHighContrast = [XueqingWindowNativeMethods]::GetHighContrastState()
    if (($liveHighContrast.Item1 -band [XueqingWindowNativeMethods]::HCF_HIGHCONTRASTON) -eq 0) {
        throw 'Windows did not report High Contrast enabled after SPI_SETHIGHCONTRAST.'
    }
    Assert-CompactStudentKeyboardJourney
    Assert-RepresentativeSurfaces
    Save-WindowScreenshot -Name 'high-contrast-1280-organization'
    Write-Host '[ux-matrix] Windows High Contrast representative journey passed with real system High Contrast enabled.'

    [XueqingWindowNativeMethods]::SetHighContrastState([uint32]$highContrastSnapshot.Item1, [string]$highContrastSnapshot.Item2)
    Restart-App

    Assert-TextScaleMatrix -RegistryPath $accessibilityPath -OriginalSnapshot $textScaleSnapshot

    Write-Host 'Windows native UX matrix passed: widths, short window, keyboard activation/focus, Chinese Unicode search, Light/Dark, High Contrast and 100/150/200/225% text scale.'
}
finally {
    try {
        Restore-RegistryValue -Path $accessibilityPath -Name 'TextScaleFactor' -Snapshot $textScaleSnapshot
        Restore-RegistryValue -Path $personalizePath -Name 'AppsUseLightTheme' -Snapshot $appsThemeSnapshot
        Restore-RegistryValue -Path $personalizePath -Name 'SystemUsesLightTheme' -Snapshot $systemThemeSnapshot
        [XueqingWindowNativeMethods]::BroadcastSettingChange('Accessibility')
        [XueqingWindowNativeMethods]::BroadcastSettingChange('ImmersiveColorSet')
        [XueqingWindowNativeMethods]::SetHighContrastState([uint32]$highContrastSnapshot.Item1, [string]$highContrastSnapshot.Item2)
    }
    catch {
        Write-Warning "Failed to fully restore isolated runner accessibility/theme state: $($_.Exception.Message)"
    }
}
