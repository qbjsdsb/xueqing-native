[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path $RepositoryRoot 'apps/windows/src/Xueqing.Windows/Xueqing.Windows.csproj'
$manifest = Join-Path $RepositoryRoot 'apps/windows/src/Xueqing.Windows/Package.appxmanifest'
$packageName = 'Xueqing.Native.Development'
$publisher = 'CN=Xueqing Native Development'
$processName = 'Xueqing.Windows'
$reportFileName = 'xueqing-app-integration.json'
$payloadMarker = 'FICTIONAL-WINDOWS-REAL-APP-DURABLE-INTENT-MARKER'
$stableOperationId = 'a67af44f-c129-4a79-82b9-b79efd7d4e50'
$workRoot = Join-Path $RepositoryRoot 'artifacts/windows-real-app-integration'

function Resolve-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'MSBuild was not found in PATH and vswhere.exe is unavailable.'
    }

    $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installationPath) {
        throw 'Visual Studio installation containing MSBuild was not found.'
    }

    $candidate = Join-Path $installationPath 'MSBuild/Current/Bin/MSBuild.exe'
    if (-not (Test-Path $candidate)) {
        throw "MSBuild path does not exist: $candidate"
    }

    return $candidate
}

function Resolve-SignTool {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $candidate = Get-ChildItem $kitsRoot -Recurse -Filter signtool.exe -ErrorAction Stop |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw 'x64 SignTool.exe was not found in the Windows SDK.'
    }

    return $candidate.FullName
}

function Set-PackageVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    [xml]$xml = Get-Content $manifest -Raw
    $xml.Package.Identity.Version = $Version
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($manifest, $settings)
    try {
        $xml.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Build-Package {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$MSBuild
    )

    Set-PackageVersion -Version $Version
    if (Test-Path $OutputDirectory) {
        Remove-Item $OutputDirectory -Recurse -Force
    }
    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null

    $buildOutput = @(
        & $MSBuild $project `
            /t:Rebuild `
            /m `
            /p:Configuration=Release `
            /p:Platform=x64 `
            /p:GenerateAppxPackageOnBuild=true `
            /p:UapAppxPackageBuildMode=SideloadOnly `
            /p:AppxBundle=Never `
            /p:AppxPackageSigningEnabled=false `
            /p:AppxPackageDir="$OutputDirectory\" `
            /p:RestoreLockedMode=true `
            /p:PublishReadyToRun=false `
            /p:PublishTrimmed=false `
            2>&1
    )
    $buildExitCode = $LASTEXITCODE
    $buildOutput | ForEach-Object { Write-Host $_ }

    if ($buildExitCode -ne 0) {
        throw "Real-app MSIX build failed for version $Version."
    }

    $packages = @(
        Get-ChildItem $OutputDirectory -Recurse -File -Filter *.msix |
            Where-Object { $_.FullName -notmatch '\\Dependencies\\' } |
            Sort-Object Length -Descending
    )

    if ($packages.Count -ne 1) {
        throw "Expected exactly one real-app MSIX for version $Version; found $($packages.Count)."
    }

    return $packages[0].FullName
}

function New-DevelopmentCertificate {
    param([Parameter(Mandatory = $true)][string]$Subject)

    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -KeyUsage DigitalSignature `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -FriendlyName 'Xueqing Native Real App Integration'

    $passwordText = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $password = ConvertTo-SecureString $passwordText -AsPlainText -Force
    $pfxPath = Join-Path $workRoot 'xueqing-native-real-app.pfx'
    $cerPath = Join-Path $workRoot 'xueqing-native-real-app.cer'

    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $password | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

    return [pscustomobject]@{
        Certificate = $certificate
        PasswordText = $passwordText
        PfxPath = $pfxPath
        CerPath = $cerPath
    }
}

function Sign-Package {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SignTool,
        [Parameter(Mandatory = $true)]$CertificateInfo
    )

    & $SignTool sign `
        /fd SHA256 `
        /a `
        /f $CertificateInfo.PfxPath `
        /p $CertificateInfo.PasswordText `
        $PackagePath

    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed for $PackagePath."
    }

    $signature = Get-AuthenticodeSignature $PackagePath
    if ($signature.Status -ne 'Valid') {
        throw "MSIX signature is not valid: $($signature.Status)"
    }
}

function Assert-NativeDllInPackage {
    param([Parameter(Mandatory = $true)][string]$PackagePath)

    $expandRoot = Join-Path $workRoot ('expanded-' + [Guid]::NewGuid().ToString('N'))
    $zipPath = Join-Path $workRoot ('package-' + [Guid]::NewGuid().ToString('N') + '.zip')
    Copy-Item $PackagePath $zipPath

    try {
        Expand-Archive -Path $zipPath -DestinationPath $expandRoot -Force
        $nativeDlls = @(
            Get-ChildItem $expandRoot -Recurse -File -Filter *.dll |
                Where-Object { $_.Name -match 'sqlite3mc' }
        )

        if ($nativeDlls.Count -lt 1) {
            throw 'SQLite3MC native DLL was not found inside the real-app MSIX.'
        }

        return @($nativeDlls | Select-Object -ExpandProperty FullName)
    }
    finally {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item $expandRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-ExistingPackage {
    Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue |
        ForEach-Object {
            Get-Process -Name $processName -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop
        }
}

function Install-Package {
    param([Parameter(Mandatory = $true)][string]$PackagePath)

    Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown -ErrorAction Stop
    $package = Get-AppxPackage -Name $packageName -ErrorAction Stop

    if ($null -eq $package) {
        throw "Installed package '$packageName' was not registered."
    }

    $installedNativeDlls = @(
        Get-ChildItem $package.InstallLocation -Recurse -File -Filter *.dll |
            Where-Object { $_.Name -match 'sqlite3mc' }
    )

    if ($installedNativeDlls.Count -lt 1) {
        throw 'SQLite3MC native DLL was not found in the installed real-app package.'
    }

    return $package
}

function Launch-And-WaitForReport {
    param(
        [Parameter(Mandatory = $true)]$Package,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ExpectedDisposition,
        [string]$ExpectedInstallationId,
        [string]$ExpectedDatabaseRelativePath
    )

    $localStateRoot = Join-Path $env:LOCALAPPDATA "Packages/$($Package.PackageFamilyName)/LocalState"
    $reportPath = Join-Path $localStateRoot $reportFileName
    $executable = Join-Path $Package.InstallLocation "$processName.exe"

    if (-not (Test-Path $executable)) {
        throw "Installed executable was not found: $executable"
    }

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $env:XUEQING_WINDOWS_INTEGRATION_PROBE = '1'
    try {
        Start-Process -FilePath $executable | Out-Null
    }
    finally {
        Remove-Item Env:\XUEQING_WINDOWS_INTEGRATION_PROBE -ErrorAction SilentlyContinue
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    do {
        if (Test-Path $reportPath) {
            $report = Get-Content $reportPath -Raw | ConvertFrom-Json
            if ($report.packageVersion -eq $ExpectedVersion) {
                if (-not $report.succeeded) {
                    throw "Real-app integration probe failed with '$($report.failureType)'."
                }

                if ($report.packageName -ne $packageName) {
                    throw "Unexpected package name '$($report.packageName)'."
                }

                if ($report.packageFamilyName -ne $Package.PackageFamilyName) {
                    throw 'Real-app report package family does not match the installed package.'
                }

                if ($report.stableOperationId -ne $stableOperationId) {
                    throw 'Stable Outbox operation id changed.'
                }

                if ([int64]$report.outboxCount -ne 1) {
                    throw "Expected one durable Outbox operation; got '$($report.outboxCount)'."
                }

                if ($report.queueStatus -ne 'Pending') {
                    throw "Expected pending Outbox state; got '$($report.queueStatus)'."
                }

                if ($report.enqueueDisposition -ne $ExpectedDisposition) {
                    throw "Expected enqueue disposition '$ExpectedDisposition'; got '$($report.enqueueDisposition)'."
                }

                if (-not $report.installationId) {
                    throw 'Real-app report omitted installation id.'
                }

                if (-not $report.databaseRelativePath) {
                    throw 'Real-app report omitted database relative path.'
                }

                if ($ExpectedInstallationId -and $report.installationId -ne $ExpectedInstallationId) {
                    throw 'Installation id changed across MSIX upgrade.'
                }

                if ($ExpectedDatabaseRelativePath -and $report.databaseRelativePath -ne $ExpectedDatabaseRelativePath) {
                    throw 'Scoped Durable Intent database path changed across MSIX upgrade.'
                }

                $relativePath = [string]$report.databaseRelativePath
                if (-not $relativePath.StartsWith('scoped-local-data/v1/', [StringComparison]::Ordinal)) {
                    throw "Unexpected scoped LocalState layout '$relativePath'."
                }

                if ($relativePath.Contains('user-fictional-001', [StringComparison]::OrdinalIgnoreCase) -or
                    $relativePath.Contains('org-fictional-001', [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Raw user or organization identifiers leaked into the LocalState path.'
                }

                return [pscustomobject]@{
                    Report = $report
                    ReportPath = $reportPath
                    LocalStateRoot = $localStateRoot
                }
            }
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Real app did not produce version $ExpectedVersion integration report within 45 seconds."
}

function Assert-EncryptedLocalFiles {
    param(
        [Parameter(Mandatory = $true)][string]$LocalStateRoot,
        [Parameter(Mandatory = $true)][string]$DatabaseRelativePath
    )

    $databasePath = Join-Path $LocalStateRoot $DatabaseRelativePath
    $keyPath = $databasePath + '.key'

    if (-not (Test-Path $databasePath)) {
        throw "Encrypted Durable Intent database does not exist: $databasePath"
    }

    if (-not (Test-Path $keyPath)) {
        throw "DPAPI wrapped-key sidecar does not exist: $keyPath"
    }

    $markerBytes = [System.Text.Encoding]::UTF8.GetBytes($payloadMarker)
    foreach ($path in @($databasePath, $databasePath + '-wal')) {
        if (-not (Test-Path $path)) {
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($path)
        for ($index = 0; $index -le $bytes.Length - $markerBytes.Length; $index++) {
            $match = $true
            for ($markerIndex = 0; $markerIndex -lt $markerBytes.Length; $markerIndex++) {
                if ($bytes[$index + $markerIndex] -ne $markerBytes[$markerIndex]) {
                    $match = $false
                    break
                }
            }

            if ($match) {
                throw "Fictional Outbox marker was visible in plaintext in '$path'."
            }
        }
    }

    return [pscustomobject]@{
        DatabasePath = $databasePath
        KeyPath = $keyPath
    }
}

function Get-DirectorySizeBytes {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-ChildItem $Path -Recurse -File | Measure-Object -Property Length -Sum).Sum
}

New-Item $workRoot -ItemType Directory -Force | Out-Null

$msbuild = Resolve-MSBuild
$signTool = Resolve-SignTool
$certificateInfo = $null
$reportPath = $null
$databasePath = $null
$originalManifest = Get-Content $manifest -Raw

try {
    Write-Host "MSBuild: $msbuild"
    Write-Host "SignTool: $signTool"

    Remove-ExistingPackage
    $certificateInfo = New-DevelopmentCertificate -Subject $publisher

    $v1Package = Build-Package `
        -Version '1.0.0.0' `
        -OutputDirectory (Join-Path $workRoot 'v1') `
        -MSBuild $msbuild
    $null = Assert-NativeDllInPackage -PackagePath $v1Package
    Sign-Package -PackagePath $v1Package -SignTool $signTool -CertificateInfo $certificateInfo

    $installedV1 = Install-Package -PackagePath $v1Package
    if ($installedV1.Version.ToString() -ne '1.0.0.0') {
        throw "Installed v1 package version mismatch: $($installedV1.Version)"
    }

    $packageFamilyName = $installedV1.PackageFamilyName
    $v1Launch = Launch-And-WaitForReport `
        -Package $installedV1 `
        -ExpectedVersion '1.0.0.0' `
        -ExpectedDisposition 'Inserted'
    $reportPath = $v1Launch.ReportPath

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $v1Local = Assert-EncryptedLocalFiles `
        -LocalStateRoot $v1Launch.LocalStateRoot `
        -DatabaseRelativePath ([string]$v1Launch.Report.databaseRelativePath)
    $databasePath = $v1Local.DatabasePath

    $v2Package = Build-Package `
        -Version '1.0.1.0' `
        -OutputDirectory (Join-Path $workRoot 'v2') `
        -MSBuild $msbuild
    $v2Native = Assert-NativeDllInPackage -PackagePath $v2Package
    Sign-Package -PackagePath $v2Package -SignTool $signTool -CertificateInfo $certificateInfo

    Add-AppxPackage -Path $v2Package -ForceApplicationShutdown -ErrorAction Stop
    $installedV2 = Get-AppxPackage -Name $packageName -ErrorAction Stop

    if ($installedV2.Version.ToString() -ne '1.0.1.0') {
        throw "Installed v2 package version mismatch: $($installedV2.Version)"
    }

    if ($installedV2.PackageFamilyName -ne $packageFamilyName) {
        throw 'Package family changed across real-app MSIX upgrade.'
    }

    $v2Launch = Launch-And-WaitForReport `
        -Package $installedV2 `
        -ExpectedVersion '1.0.1.0' `
        -ExpectedDisposition 'AlreadyPresent' `
        -ExpectedInstallationId ([string]$v1Launch.Report.installationId) `
        -ExpectedDatabaseRelativePath ([string]$v1Launch.Report.databaseRelativePath)

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $null = Assert-EncryptedLocalFiles `
        -LocalStateRoot $v2Launch.LocalStateRoot `
        -DatabaseRelativePath ([string]$v2Launch.Report.databaseRelativePath)

    $v1Bytes = (Get-Item $v1Package).Length
    $v2Bytes = (Get-Item $v2Package).Length
    $installedBytes = Get-DirectorySizeBytes -Path $installedV2.InstallLocation

    Write-Host "package family: $packageFamilyName"
    Write-Host "installation id preserved: $($v2Launch.Report.installationId)"
    Write-Host "scoped database path preserved: $($v2Launch.Report.databaseRelativePath)"
    Write-Host "stable operation id preserved: $($v2Launch.Report.stableOperationId)"
    Write-Host "MSIX SQLite3MC entries: $($v2Native -join ', ')"

    if ($env:GITHUB_STEP_SUMMARY) {
        @"
## Windows real-app LocalState + MSIX integration

- Package family: ``$packageFamilyName``
- v1 package: ``1.0.0.0`` — $([math]::Round($v1Bytes / 1MB, 2)) MiB
- v2 package: ``1.0.1.0`` — $([math]::Round($v2Bytes / 1MB, 2)) MiB
- Installed footprint: $([math]::Round($installedBytes / 1MB, 2)) MiB
- Real ``Xueqing.Windows`` used ``ApplicationData.Current.LocalFolder``: yes
- Scope dimensions represented in hashed LocalState layout: environment + app user + organization + installation
- DPAPI wrapped-key sidecar present: yes
- Fictional Outbox marker absent from DB/WAL plaintext scan: yes
- Stable ``operation_id`` survived in-place MSIX upgrade without duplicate enqueue: yes
- Installation id and encrypted database path survived upgrade: yes
- SQLite3MC native DLL present in MSIX and installed package: yes
"@ | Add-Content $env:GITHUB_STEP_SUMMARY
    }

    Remove-AppxPackage -Package $installedV2.PackageFullName -ErrorAction Stop

    $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while (((Test-Path $reportPath) -or (Test-Path $databasePath)) -and [DateTimeOffset]::UtcNow -lt $cleanupDeadline) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-Path $reportPath) {
        throw 'MSIX uninstall left the real-app LocalState report behind.'
    }

    if (Test-Path $databasePath) {
        throw 'MSIX uninstall left the encrypted Durable Intent database behind.'
    }

    Write-Host 'MSIX uninstall removed the real-app LocalState integration data.'
}
finally {
    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Remove-ExistingPackage
    Set-Content -Path $manifest -Value $originalManifest -Encoding utf8NoBOM

    if ($null -ne $certificateInfo) {
        Remove-Item "Cert:\CurrentUser\My\$($certificateInfo.Certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
        Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' |
            Where-Object { $_.Thumbprint -eq $certificateInfo.Certificate.Thumbprint } |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
}
