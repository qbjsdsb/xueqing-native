[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path $RepositoryRoot 'apps/windows/spikes/Xueqing.Windows.PackagingProbe/Xueqing.Windows.PackagingProbe.csproj'
$manifest = Join-Path $RepositoryRoot 'apps/windows/spikes/Xueqing.Windows.PackagingProbe/Package.appxmanifest'
$packageName = 'Xueqing.Native.PackagingProbe'
$publisher = 'CN=Xueqing Native Development'
$processName = 'Xueqing.Windows.PackagingProbe'
$workRoot = Join-Path $RepositoryRoot 'artifacts/msix-packaging-spike'

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
        /p:PublishReadyToRun=false `
        /p:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) {
        throw "MSIX build failed for version $Version."
    }

    $packages = @(
        Get-ChildItem $OutputDirectory -Recurse -File -Filter *.msix |
            Where-Object { $_.FullName -notmatch '\\Dependencies\\' } |
            Sort-Object Length -Descending
    )

    if ($packages.Count -ne 1) {
        throw "Expected exactly one application MSIX for version $Version; found $($packages.Count)."
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
        -FriendlyName 'Xueqing Native MSIX Spike'

    $passwordText = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $password = ConvertTo-SecureString $passwordText -AsPlainText -Force
    $pfxPath = Join-Path $workRoot 'xueqing-native-spike.pfx'
    $cerPath = Join-Path $workRoot 'xueqing-native-spike.cer'

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
            throw 'SQLite3MC native DLL was not found inside the generated MSIX.'
        }

        return @($nativeDlls | Select-Object -ExpandProperty FullName)
    }
    finally {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item $expandRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-ExistingProbePackage {
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
        throw 'SQLite3MC native DLL was not found in the installed MSIX location.'
    }

    return $package
}

function Launch-And-WaitForReport {
    param(
        [Parameter(Mandatory = $true)]$Package,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [string]$ExpectedStableToken
    )

    $reportPath = Join-Path $env:LOCALAPPDATA "Packages/$($Package.PackageFamilyName)/LocalState/packaging-probe.json"
    $executable = Join-Path $Package.InstallLocation "$processName.exe"

    if (-not (Test-Path $executable)) {
        throw "Installed executable was not found: $executable"
    }

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Start-Process -FilePath $executable | Out-Null

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        if (Test-Path $reportPath) {
            $report = Get-Content $reportPath -Raw | ConvertFrom-Json
            if ($report.packageVersion -eq $ExpectedVersion) {
                if ($report.packageFamilyName -ne $Package.PackageFamilyName) {
                    throw 'Application report package family does not match the installed package.'
                }

                if ($report.sqlite3McVersion -ne 'SQLite3 Multiple Ciphers 2.4.0') {
                    throw "Unexpected installed SQLite3MC runtime '$($report.sqlite3McVersion)'."
                }

                if ($ExpectedStableToken -and $report.stableToken -ne $ExpectedStableToken) {
                    throw 'Stable encrypted local-data token changed across MSIX upgrade.'
                }

                return [pscustomobject]@{
                    Report = $report
                    ReportPath = $reportPath
                }
            }
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Packaging probe did not produce version $ExpectedVersion report within 30 seconds."
}

function Get-DirectorySizeBytes {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-ChildItem $Path -Recurse -File | Measure-Object -Property Length -Sum).Sum
}

New-Item $workRoot -ItemType Directory -Force | Out-Null

$msbuild = Resolve-MSBuild
$signTool = Resolve-SignTool
$certificateInfo = $null
$v1Package = $null
$v2Package = $null
$packageFamilyName = $null
$reportPath = $null

try {
    Write-Host "MSBuild: $msbuild"
    Write-Host "SignTool: $signTool"

    Remove-ExistingProbePackage
    $certificateInfo = New-DevelopmentCertificate -Subject $publisher

    $v1Package = Build-Package `
        -Version '1.0.0.0' `
        -OutputDirectory (Join-Path $workRoot 'v1') `
        -MSBuild $msbuild
    $v1Native = Assert-NativeDllInPackage -PackagePath $v1Package
    Sign-Package -PackagePath $v1Package -SignTool $signTool -CertificateInfo $certificateInfo

    $installedV1 = Install-Package -PackagePath $v1Package
    if ($installedV1.Version.ToString() -ne '1.0.0.0') {
        throw "Installed v1 package version mismatch: $($installedV1.Version)"
    }

    $packageFamilyName = $installedV1.PackageFamilyName
    $v1Launch = Launch-And-WaitForReport -Package $installedV1 -ExpectedVersion '1.0.0.0'
    $reportPath = $v1Launch.ReportPath
    $stableToken = [string]$v1Launch.Report.stableToken

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

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
        throw 'Package family changed across MSIX upgrade.'
    }

    $v2Launch = Launch-And-WaitForReport `
        -Package $installedV2 `
        -ExpectedVersion '1.0.1.0' `
        -ExpectedStableToken $stableToken

    $v1Bytes = (Get-Item $v1Package).Length
    $v2Bytes = (Get-Item $v2Package).Length
    $installedBytes = Get-DirectorySizeBytes -Path $installedV2.InstallLocation

    Write-Host "v1 package bytes: $v1Bytes"
    Write-Host "v2 package bytes: $v2Bytes"
    Write-Host "installed bytes: $installedBytes"
    Write-Host "package family: $packageFamilyName"
    Write-Host "stable token preserved: $stableToken"
    Write-Host "MSIX SQLite3MC entries: $($v2Native -join ', ')"

    if ($env:GITHUB_STEP_SUMMARY) {
        @"
## Windows MSIX packaging spike

- Package family: ``$packageFamilyName``
- v1 package: ``1.0.0.0`` — $([math]::Round($v1Bytes / 1MB, 2)) MiB
- v2 package: ``1.0.1.0`` — $([math]::Round($v2Bytes / 1MB, 2)) MiB
- Installed footprint: $([math]::Round($installedBytes / 1MB, 2)) MiB
- SQLite3MC runtime: ``$($v2Launch.Report.sqlite3McVersion)``
- Encrypted LocalState stable token preserved across upgrade: yes
- SQLite3MC native DLL present in MSIX and installed package: yes
"@ | Add-Content $env:GITHUB_STEP_SUMMARY
    }

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Remove-AppxPackage -Package $installedV2.PackageFullName -ErrorAction Stop

    $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ((Test-Path $reportPath) -and [DateTimeOffset]::UtcNow -lt $cleanupDeadline) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-Path $reportPath) {
        throw 'MSIX uninstall left the packaging probe LocalState report behind.'
    }

    Write-Host 'MSIX uninstall removed the packaging probe LocalState report.'
}
finally {
    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Remove-ExistingProbePackage

    if ($null -ne $certificateInfo) {
        Remove-Item "Cert:\CurrentUser\My\$($certificateInfo.Certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
        Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' |
            Where-Object { $_.Thumbprint -eq $certificateInfo.Certificate.Thumbprint } |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
}
