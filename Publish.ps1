param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [ValidateSet('Validate','Publish','NewVersion','Update')][string]$Command = 'Validate'
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
& (Join-Path $PSScriptRoot 'Check-Publication.ps1') -PackagePath $package
[xml]$configuration = Get-Content -LiteralPath (Join-Path $package 'PublishConfiguration.xml') -Raw
if ($Command -eq 'Validate') { return }
if ($configuration.Publish.AccessLevel.Value -notin @('Private','Public')) { throw 'Use an explicit Private or Public access level.' }
$modId = [string]$configuration.Publish.ModId.Value
if ($Command -eq 'Publish' -and ![string]::IsNullOrWhiteSpace($modId)) { throw 'Publish creates a new mod; use NewVersion or Update for an existing ModId.' }
if ($Command -ne 'Publish' -and [string]::IsNullOrWhiteSpace($modId)) { throw 'An existing platform ModId is required for updates.' }
$publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH','Process')
if ([string]::IsNullOrWhiteSpace($publisher)) { $publisher = [Environment]::GetEnvironmentVariable('CSII_MODPUBLISHERPATH','User') }
if ([string]::IsNullOrWhiteSpace($publisher) -or !(Test-Path -LiteralPath $publisher -PathType Leaf)) { throw 'Official Mod Publisher not found; configure the installed game modding toolchain.' }
Push-Location $package
try {
    $arguments = @($Command,(Join-Path $package 'PublishConfiguration.xml'),'-c',(Join-Path $package 'content'),'-v')
    & $publisher @arguments
    if ($LASTEXITCODE -ne 0) { throw ('Official Mod Publisher exited with code ' + $LASTEXITCODE) }
} finally { Pop-Location }
