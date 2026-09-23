param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$UnsignedMsixPath,
    [Parameter(Mandatory = $true)][string]$Publisher,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path $RepositoryRoot).Path
$unsigned = (Resolve-Path $UnsignedMsixPath).Path
$work = Join-Path $env:RUNNER_TEMP 'xueqing-setup-dry-run'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item $work -ItemType Directory -Force | Out-Null
$signed = Join-Path $work 'Xueqing.Native.test-signed.msix'
Copy-Item $unsigned $signed

$passwordText = [Guid]::NewGuid().ToString('N') + '!xQ1'
$password = ConvertTo-SecureString $passwordText -AsPlainText -Force
$certificate = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -KeyExportPolicy Exportable -CertStoreLocation 'Cert:\CurrentUser\My' -HashAlgorithm sha256
$pfx = Join-Path $work 'xueqing-test.pfx'
$cer = Join-Path $work 'xueqing-test.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cer | Out-Null
$trusted = Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'

try {
    $signtool = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Recurse -Filter signtool.exe | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $signtool) { throw 'SignTool not found.' }
    & $signtool.FullName sign /fd SHA256 /f $pfx /p $passwordText $signed
    if ($LASTEXITCODE -ne 0) { throw 'Development signing for setup dry run failed.' }

    $signature = Get-AuthenticodeSignature $signed
    if ($signature.Status -ne 'Valid') { throw "Test MSIX signature is $($signature.Status)." }
    if ($signature.SignerCertificate.Subject -ne $Publisher) { throw 'Test signer Subject does not match Publisher.' }

    & (Join-Path $repo 'tools/release/build-windows-setup.ps1') -RepositoryRoot $repo -MsixPath $signed -Publisher $Publisher -OutputDirectory $OutputDirectory

    $setup = Get-ChildItem $OutputDirectory -Filter XueqingSetup-*.exe | Select-Object -First 1
    if ($null -eq $setup) { throw 'Setup dry-run executable not found.' }

    Get-AppxPackage -Name Xueqing.Native -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop
    }

    $process = Start-Process -FilePath $setup.FullName -ArgumentList @("--quiet","--no-launch") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "XueqingSetup dry run failed with exit code $($process.ExitCode)." }

    $installed = Get-AppxPackage -Name Xueqing.Native -ErrorAction Stop
    if ($installed.Publisher -ne $Publisher) { throw 'Installed Setup dry-run Publisher mismatch.' }
    Write-Host "XueqingSetup dry run installed production package identity successfully: $($installed.PackageFullName)"
}
finally {
    Get-AppxPackage -Name Xueqing.Native -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue
    }
    if ($null -ne $trusted) { Remove-Item "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)" -Force -ErrorAction SilentlyContinue }
    if ($null -ne $certificate) { Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue }
}
