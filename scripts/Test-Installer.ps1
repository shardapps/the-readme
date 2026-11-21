[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run the installer smoke test on an isolated GitHub Actions runner.' }
$Installer = (Resolve-Path -LiteralPath $Installer).Path
$destination = Join-Path $env:RUNNER_TEMP "The Readme smoke"
$integration = 'HKCU:\Software\ShardApps\TheReadme'
$registered = 'HKCU:\Software\RegisteredApplications'
$progId = 'ShardApps.TheReadme.Document'
$previousPath = (Get-ItemProperty -Path 'HKCU:\Environment' -Name Path -ErrorAction SilentlyContinue).Path

function In-UserPath {
    $entries = (Get-ItemProperty -Path 'HKCU:\Environment' -Name Path -ErrorAction SilentlyContinue).Path -split ';'
    return $entries -contains $destination
}

try {
    $setup = Start-Process -FilePath $Installer -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=path', "/DIR=`"$destination`"", "/LOG=`"$env:RUNNER_TEMP/setup.log`""
    ) -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Setup exited with $($setup.ExitCode)." }
    $app = Join-Path $destination 'Readme.exe'
    if (!(Test-Path -LiteralPath $app)) { throw 'Installed executable was not found.' }
    $registration = Get-ItemPropertyValue -Path $registered -Name 'The Readme'
    if ($registration -ne 'Software\ShardApps\TheReadme\Capabilities') { throw 'Default Apps registration is missing.' }
    $associations = Get-ItemProperty "$integration\Capabilities\FileAssociations"
    if ($associations.'.md' -ne $progId -or $associations.'.txt' -ne $progId -or $associations.'.hdl' -ne $progId) {
        throw 'File association registration is incomplete.'
    }
    $command = (Get-Item "HKCU:\Software\Classes\$progId\shell\open\command").GetValue('')
    if ($command -ne "`"$app`" -- `"%1`"") { throw 'File open command is not quoted correctly.' }
    if (!(In-UserPath)) { throw 'Setup did not add the requested user PATH entry.' }

    $fixture = Join-Path $env:RUNNER_TEMP 'Path test README.md'
    '# Installed app test' | Set-Content -LiteralPath $fixture -Encoding utf8
    $process = Start-Process -FilePath $app -ArgumentList "-- `"$fixture`"" -PassThru
    try {
        $ready = $process.WaitForInputIdle(15000)
        if (!$ready -or $process.HasExited) { throw 'The installed GUI did not start.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.MainWindowTitle -like 'Path test README.md*') { break }
            if ($process.HasExited) { throw 'The installed GUI exited before opening the file.' }
            Start-Sleep -Milliseconds 150
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowTitle -notlike 'Path test README.md*') { throw 'The installed app did not open its command-line file.' }
    }
    finally {
        if (!$process.HasExited -and (!$process.CloseMainWindow() -or !$process.WaitForExit(5000))) {
            Stop-Process -Id $process.Id -Force
        }
    }
}
finally {
    $uninstaller = Get-ChildItem -LiteralPath $destination -Filter 'unins*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uninstaller) {
        $removed = Start-Process -FilePath $uninstaller.FullName -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$env:RUNNER_TEMP/uninstall.log`""
        ) -WindowStyle Hidden -Wait -PassThru
        if ($removed.ExitCode -ne 0) { throw "Uninstaller exited with $($removed.ExitCode)." }
    }
}
if (In-UserPath) { throw 'Uninstall left the app directory on PATH.' }
if (Test-Path -LiteralPath $integration) { throw 'Uninstall left app registration behind.' }
$remainingPath = (Get-ItemProperty -Path 'HKCU:\Environment' -Name Path -ErrorAction SilentlyContinue).Path
if ([string]$remainingPath -cne [string]$previousPath) { throw 'Uninstall changed unrelated PATH entries.' }
Write-Host 'Installer, app startup, default registration, PATH and uninstall checks passed.'
