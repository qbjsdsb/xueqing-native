param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
$project = Join-Path $repo 'apps/windows/src/Xueqing.Windows/Xueqing.Windows.csproj'
$manifest = Join-Path $repo 'apps/windows/src/Xueqing.Windows/Package.appxmanifest'
$renderedManifest = Join-Path $env:RUNNER_TEMP 'xueqing-production-Package.appxmanifest'
$profile = Join-Path $repo 'apps/windows/src/Xueqing.Windows/Deployment/deployment_profile.json'

function Read-ReleaseProperties {
    $values = @{}
    foreach ($line in Get-Content (Join-Path $repo 'release/version.properties')) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        $parts = $trimmed.Split('=', 2)
        if ($parts.Count -ne 2) { throw "Invalid release property: $line" }
        $values[$parts[0]] = $parts[1]
    }
    return $values
}

function Resolve-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installationPath) { throw 'MSBuild installation not found.' }
    return (Join-Path $installationPath 'MSBuild/Current/Bin/MSBuild.exe')
}

$props = Read-ReleaseProperties
$releaseVersion = $props['releaseVersion']
$expectedPackageVersion = $props['windowsPackageVersion']
$originalManifest = Get-Content $manifest -Raw

try {
    python -B (Join-Path $repo 'tools/release/render_windows_manifest.py') --publisher $Publisher --output $renderedManifest
    if ($LASTEXITCODE -ne 0) { throw 'Production manifest rendering failed.' }
    Copy-Item $renderedManifest $manifest -Force

    $sourceCommit = (git -C $repo rev-parse HEAD).Trim()
    python -B (Join-Path $repo 'tools/deployment/stage_deployment_profile.py') (Join-Path $repo 'deployment/production_profile.template.json') $sourceCommit $profile
    if ($LASTEXITCODE -ne 0) { throw 'Deployment profile staging failed.' }

    $msbuild = Resolve-MSBuild
    & $msbuild $project /t:Restore /p:RestoreLockedMode=true /p:Configuration=Release /p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Windows release restore failed.' }

    $packageDir = Join-Path $env:RUNNER_TEMP 'xueqing-windows-release-package'
    if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
    New-Item $packageDir -ItemType Directory -Force | Out-Null

    & $msbuild $project /t:Rebuild /m /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false /p:AppxPackageDir="$packageDir\" /p:RestoreLockedMode=true /p:PublishReadyToRun=false /p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw 'Windows production-shaped MSIX build failed.' }

    $packages = @(Get-ChildItem $packageDir -Recurse -File -Filter *.msix | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
    if ($packages.Count -ne 1) { throw "Expected exactly one production-shaped MSIX; found $($packages.Count)." }

    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
    $destination = Join-Path $OutputDirectory "Xueqing-$releaseVersion-x64-unsigned.msix"
    Copy-Item $packages[0].FullName $destination -Force

    $zip = Join-Path $env:RUNNER_TEMP 'xueqing-release-msix.zip'
    $expand = Join-Path $env:RUNNER_TEMP 'xueqing-release-msix-expanded'
    Copy-Item $destination $zip -Force
    if (Test-Path $expand) { Remove-Item $expand -Recurse -Force }
    Expand-Archive $zip $expand -Force
    [xml]$packagedManifest = Get-Content (Join-Path $expand 'AppxManifest.xml') -Raw
    if ($packagedManifest.Package.Identity.Name -ne 'Xueqing.Native') { throw 'Built MSIX package name drifted.' }
    if ($packagedManifest.Package.Identity.Publisher -ne $Publisher) { throw 'Built MSIX Publisher drifted.' }
    if ($packagedManifest.Package.Identity.Version -ne $expectedPackageVersion) { throw 'Built MSIX version drifted.' }

    $hash = Get-FileHash $destination -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($destination))" | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS-windows.txt') -Encoding ascii
    Write-Host "Production-shaped unsigned MSIX ready: $destination"
}
finally {
    Set-Content -Path $manifest -Value $originalManifest -Encoding utf8 -NoNewline
    Remove-Item $profile -Force -ErrorAction SilentlyContinue
}
