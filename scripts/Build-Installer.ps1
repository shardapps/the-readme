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
$version = ([xml](Get-Content -LiteralPath $project -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$script = Join-Path $root 'installer/TheReadme.iss'
if (!$BuildArtifactsPath) { $BuildArtifactsPath = Join-Path $root "artifacts/installer-build/$version" }

foreach ($target in $Rid) {
    $destination = Join-Path $root "artifacts/publish/$version/$target"
    $arguments = @('publish', $project, '-c', 'Release', '-r', $target, '-o', $destination,
        '-p:RestoreLockedMode=true', '-p:DebugType=None',
        '--artifacts-path', (Join-Path $BuildArtifactsPath $target))
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $target." }
    & $compiler "/DRid=$target" "/DVersion=$version" $script
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed for $target." }
}

$output = Join-Path $root 'artifacts/installers'
$lines = foreach ($target in $Rid) {
    $file = Join-Path $output "The-Readme-$version-$target-setup.exe"
    if ((Get-Item -LiteralPath $file).Length -ge 50000000) { throw "$target setup exceeded the 50 MB release budget." }
    $digest = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  $(Split-Path -Leaf $file)"
}
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", [Text.Encoding]::ASCII)
