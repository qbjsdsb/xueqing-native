param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$MsixPath,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
$msix = (Resolve-Path $MsixPath).Path
$project = Join-Path $repo 'apps/windows/src/Xueqing.Setup/Xueqing.Setup.csproj'
$properties = @{}
foreach ($line in Get-Content (Join-Path $repo 'release/version.properties')) {
    $trimmed = $line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
    $parts = $trimmed.Split('=', 2)
    if ($parts.Count -ne 2) { throw "Invalid release property: $line" }
    $properties[$parts[0]] = $parts[1]
}
$releaseVersion = $properties['releaseVersion']
$hash = (Get-FileHash $msix -Algorithm SHA256).Hash.ToLowerInvariant()

$publishDir = Join-Path $env:RUNNER_TEMP 'xueqing-setup-publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=true `
    /p:PublishReadyToRun=false `
    /p:PublishTrimmed=false `
    /p:XueqingEmbeddedMsix="$msix" `
    /p:XueqingPayloadSha256="$hash" `
    /p:XueqingPublisher="$Publisher"
if ($LASTEXITCODE -ne 0) { throw 'XueqingSetup publish failed.' }

$exe = Join-Path $publishDir 'XueqingSetup.exe'
if (-not (Test-Path $exe)) { throw 'Published XueqingSetup.exe was not found.' }
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$destination = Join-Path $OutputDirectory "XueqingSetup-$releaseVersion-x64.exe"
Copy-Item $exe $destination -Force

$setupHash = Get-FileHash $destination -Algorithm SHA256
"$($setupHash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($destination))" | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS-setup.txt') -Encoding ascii
Write-Host "Setup bootstrapper ready: $destination"
