[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Rid = @('win-x64', 'win-arm64'),
    [string]$Dotnet = 'dotnet',
    [string]$BuildArtifactsPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $root '.tools/inno/ISCC.exe'
if (!(Test-Path -LiteralPath $compiler)) { & "$PSScriptRoot/Install-Inno.ps1" }
$project = Join-Path $root 'src/TheReadme/TheReadme.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$script = Join-Path $root 'installer/TheReadme.iss'

foreach ($target in $Rid) {
    $destination = Join-Path $root "artifacts/publish/$target"
    $arguments = @('publish', $project, '-c', 'Release', '-r', $target, '--no-self-contained',
        '-o', $destination, '-p:RestoreLockedMode=true', '-p:DebugType=None')
    if ($BuildArtifactsPath) { $arguments += @('--artifacts-path', $BuildArtifactsPath) }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $target." }
    & $compiler "/DRid=$target" "/DVersion=$version" $script
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed for $target." }
}

$output = Join-Path $root 'artifacts/installers'
$lines = foreach ($target in $Rid) {
    $file = Join-Path $output "The-Readme-$version-$target-setup.exe"
    $digest = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  $(Split-Path -Leaf $file)"
}
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", [Text.Encoding]::ASCII)
