param(
    [string]$UserDataPath = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'),
    [switch]$WithMcp,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
function Get-PackageHash([string]$LiteralPath) {
    $stream = [IO.File]::OpenRead($LiteralPath)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Assert-Closed {
    if (Get-Process Cities2 -ErrorAction SilentlyContinue) { throw 'Save and fully close Cities: Skylines II before installation.' }
}
function Resolve-Child([string]$Root, [string]$Relative) {
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $resolvedChild = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $Relative))
    if (!$resolvedChild.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Path escaped installation root: ' + $Relative)
    }
    $resolvedChild
}
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'package-complete.json'))) { throw 'Package is incomplete.' }
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'Passenger Helicopters' -or $manifest.version -notin @('1.0.0','1.0.1','1.0.2','1.0.3','1.0.4','1.0.5','1.0.6','1.0.7','1.0.8','1.0.9','1.1.0','1.1.1','1.1.2','1.1.3', '1.1.4','1.1.5','1.1.6','1.1.7','1.1.8','1.1.9','1.1.10')) { throw 'Unexpected package manifest.' }
$gameRoot = [IO.Path]::GetFullPath($UserDataPath)
if (!(Test-Path -LiteralPath $gameRoot -PathType Container)) { throw 'Game user-data directory does not exist.' }
if (!$VerifyOnly) { Assert-Closed }

# Verify the entire package before backing up or changing any game file.
$runtimeInEntry = Test-Path -LiteralPath (Resolve-Child $gameRoot 'Mods/PassengerHelicopters/CityWeaver.dll')
$runtimeInLegacy = Test-Path -LiteralPath (Resolve-Child $gameRoot 'Mods/CityWeaver/CityWeaver.dll')
if ($runtimeInEntry -and $runtimeInLegacy) { throw 'Duplicate CityWeaver runtime copies found; resolve the duplicate before installing.' }
if ($manifest.code_only -and (!(Test-Path -LiteralPath (Resolve-Child $gameRoot 'Mods/PassengerHelicopters/PassengerHelicopters.dll')) -or
    !($runtimeInEntry -or $runtimeInLegacy) -or !(Test-Path -LiteralPath (Resolve-Child $gameRoot 'ImportedData') -PathType Container))) {
    throw 'A code-only update requires an existing Passenger Helicopters installation and imported assets.'
}
$selected = @()
foreach ($record in $manifest.files) {
    $source = Resolve-Child $PSScriptRoot $record.path
    if ((Get-Item -LiteralPath $source).Length -ne $record.bytes -or
        (Get-PackageHash $source) -ne $record.sha256) { throw ('Package checksum mismatch: ' + $record.path) }
    if ($record.kind -eq 'mcp_adapter' -and !$WithMcp) { continue }
    $relative = [string]$record.path
    if ($record.kind -eq 'mcp_adapter') { $relative = $relative.Substring('MCP-Adapter/'.Length) }
    # Preserve an existing consolidated standalone install; do not create a
    # second compatibility assembly in Mods/CityWeaver during an update.
    if ($runtimeInEntry -and $relative.StartsWith('Mods/CityWeaver/', [StringComparison]::OrdinalIgnoreCase)) {
        $relative = 'Mods/PassengerHelicopters/' + $relative.Substring('Mods/CityWeaver/'.Length)
    }
    if ($relative -notmatch '^(Mods|ImportedData)[/\\]') { throw 'Unexpected installation destination.' }
    $destination = Resolve-Child $gameRoot $relative
    if (Test-Path -LiteralPath $destination) {
        $existingHash = (Get-PackageHash $destination)
        if ($existingHash -eq $record.sha256) { continue }
        if ($record.kind -eq 'asset') { throw ('An installed asset differs from the package; stop for review: ' + $relative) }
    }
    $selected += [pscustomobject]@{ source = $source; destination = $destination; relative = $relative; sha256 = $record.sha256 }
}
if ($VerifyOnly) {
    [pscustomobject]@{ package_verified = $true; files = $manifest.files.Count; changes_needed = $selected.Count; with_mcp = [bool]$WithMcp }
    return
}
Assert-Closed
$backupRoot = Resolve-Child $gameRoot ('ModsData\PassengerHelicopters\Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$journal = [ordered]@{ version = $manifest.version; state = 'backing_up'; with_mcp = [bool]$WithMcp; original = @(); installed = @(); save_backup = $null }
$journalPath = Join-Path $backupRoot 'installation.json'
function Save-Journal { $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $journalPath -Encoding UTF8 }
Save-Journal
try {
    foreach ($record in $selected) {
        if (!(Test-Path -LiteralPath $record.destination)) { continue }
        # A backup must not look like another loadable executable asset.
        $backup = Resolve-Child $backupRoot ($record.relative + '.backup')
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($backup)) -Force | Out-Null
        $originalHash = (Get-PackageHash $record.destination)
        Copy-Item -LiteralPath $record.destination -Destination $backup
        if ((Get-PackageHash $backup) -ne $originalHash) { throw 'Backup verification failed.' }
        $journal.original += [pscustomobject]@{ destination = $record.destination; backup = $backup; sha256 = $originalHash }
    }
    $saves = Join-Path $gameRoot 'Saves'
    if (Test-Path -LiteralPath $saves) {
        $latest = Get-ChildItem -LiteralPath $saves -Filter '*.cok' -Recurse -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($latest) {
            $saveBackup = Resolve-Child $backupRoot ('Saves\' + $latest.Name)
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($saveBackup)) -Force | Out-Null
            Copy-Item -LiteralPath $latest.FullName -Destination $saveBackup
            $saveHash = (Get-PackageHash $latest.FullName)
            if ((Get-PackageHash $saveBackup) -ne $saveHash) { throw 'Save backup verification failed.' }
            if (Test-Path -LiteralPath ($latest.FullName + '.cid')) { Copy-Item -LiteralPath ($latest.FullName + '.cid') -Destination ($saveBackup + '.cid') }
            $journal.save_backup = [pscustomobject]@{ source = $latest.FullName; backup = $saveBackup; sha256 = $saveHash }
        }
    }
    Assert-Closed
    $journal.state = 'installing'; Save-Journal
    foreach ($record in $selected) {
        Assert-Closed
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($record.destination)) -Force | Out-Null
        Copy-Item -LiteralPath $record.source -Destination $record.destination -Force
        if ((Get-PackageHash $record.destination) -ne $record.sha256) { throw 'Installed file verification failed.' }
        $journal.installed += [pscustomobject]@{ destination = $record.destination; sha256 = $record.sha256 }
    }
    $journal.state = 'completed'; Save-Journal
    [pscustomobject]@{ installed = $true; version = $manifest.version; with_mcp = [bool]$WithMcp; changes = $selected.Count; backup = $backupRoot }
} catch {
    $journal.state = 'failed'; $journal.error = $_.Exception.Message; Save-Journal
    throw
}
