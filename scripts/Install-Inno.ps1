[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root '.tools/inno'
$compiler = Join-Path $destination 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return }

$tag = 'is-6_7_4'
$name = 'innosetup-6.7.4.exe'
$digest = 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'
New-Item -ItemType Directory -Force $destination | Out-Null
$installer = Join-Path $destination $name
Invoke-WebRequest "https://github.com/jrsoftware/issrc/releases/download/$tag/$name" -OutFile $installer
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $digest) {
    throw 'Inno Setup download checksum mismatch.'
}
$result = Start-Process -FilePath $installer -ArgumentList @(
    '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$destination`""
) -WindowStyle Hidden -PassThru -Wait
if ($result.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) {
    throw "Inno Setup installation failed with exit code $($result.ExitCode)."
}
