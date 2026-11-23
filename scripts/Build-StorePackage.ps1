[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [string]$BuildArtifactsPath,
    [string]$MakeAppx
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$MakeAppx) {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $MakeAppx = Get-ChildItem -LiteralPath $sdk -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/MakeAppx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$MakeAppx -or !(Test-Path -LiteralPath $MakeAppx)) {
    throw 'MakeAppx.exe from the Windows SDK is required.'
}

$storeProject = Join-Path $root 'packaging/store'
$manifestPath = Join-Path $storeProject 'AppxManifest.xml'
$project = Join-Path $root 'src/TheReadme/TheReadme.csproj'
$output = Join-Path $root 'artifacts/store'
[xml]$identityTemplate = Get-Content -LiteralPath $manifestPath -Raw
$version = $identityTemplate.Package.Identity.Version
$workspace = Join-Path $output ([guid]::NewGuid().ToString('N'))
$packages = Join-Path $workspace 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$packageLocation = & $Dotnet nuget locals global-packages --list
$packagesRoot = $packageLocation -replace '^[^:]+:\s*', ''
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $packagesRoot -PathType Container)) {
    throw 'Could not find the NuGet package cache.'
}

foreach ($architecture in @('x64', 'arm64')) {
    $publish = Join-Path $workspace $architecture
    $arguments = @('publish', $project, '-c', 'Release', '-r', "win-$architecture", '--self-contained',
        '-o', $publish, '-p:RestoreLockedMode=true', '-p:DebugType=None', '-p:DebugSymbols=false',
        '-p:SatelliteResourceLanguages=en-US')
    if ($BuildArtifactsPath) { $arguments += @('--artifacts-path', $BuildArtifactsPath) }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $architecture." }

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $manifest.Package.Identity.ProcessorArchitecture = $architecture
    $manifest.Save((Join-Path $publish 'AppxManifest.xml'))
    Copy-Item -LiteralPath (Join-Path $storeProject 'StoreAssets') -Destination $publish -Recurse
    foreach ($notice in @('LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $root $notice) -Destination $publish
    }
    $dependencies = Get-Content -LiteralPath (Join-Path $publish 'Readme.deps.json') -Raw | ConvertFrom-Json -AsHashtable
    $runtimePacks = @($dependencies.libraries.Keys | Where-Object { $_ -like 'runtimepack.*' })
    if ($runtimePacks.Count -ne 2) { throw "Expected the .NET and Windows Desktop runtime packs for $architecture." }
    foreach ($runtime in $runtimePacks) {
        $reference = $runtime.Substring('runtimepack.'.Length).ToLowerInvariant()
        $runtimeFolder = Join-Path $packagesRoot $reference
        $target = Join-Path $publish ('RuntimeNotices/' + ($reference -split '/')[0])
        $notices = @(Get-ChildItem -LiteralPath $runtimeFolder -File | Where-Object { $_.Name -match '^(licen[cs]e|third.party.notices)' })
        if (!$notices.Count) { throw "Missing runtime license files for $reference." }
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $notices | Copy-Item -Destination $target
    }

    $package = Join-Path $packages "The-Readme-$version-$architecture.msix"
    $packLog = & $MakeAppx pack /o /d $publish /p $package
    if ($LASTEXITCODE -ne 0) {
        $packLog | Select-Object -Last 30 | Write-Host
        throw "MSIX packing failed for $architecture."
    }
    Write-Host "Built $package"
}

$bundle = Join-Path $output "The-Readme-$version-store.msixbundle"
& $MakeAppx bundle /o /d $packages /p $bundle /bv $version
if ($LASTEXITCODE -ne 0) { throw 'MSIX bundling failed.' }
$digest = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$digest  $(Split-Path -Leaf $bundle)`n", [Text.Encoding]::ASCII)
