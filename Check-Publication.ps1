param([Parameter(Mandatory=$true)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
& node (Join-Path $PSScriptRoot 'publication.mjs') --verify --output $package
if ($LASTEXITCODE -ne 0) { throw 'Publication content verification failed.' }
$manifest = Get-Content -LiteralPath (Join-Path $package 'publication-manifest.json') -Raw | ConvertFrom-Json
[xml]$configuration = Get-Content -LiteralPath (Join-Path $package 'PublishConfiguration.xml') -Raw
$content = Join-Path $package 'content'
foreach ($name in @('CityWeaver','TransportFever3Helicopters.Runtime','PassengerHelicopters')) {
    $file = Get-Item -LiteralPath (Join-Path $content ($name + '.dll'))
    $identity = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
    $expectedAssemblyVersion = if ($name -eq 'CityWeaver') { '1.36.100.0' } else { $manifest.version + '.0' }
    if ($identity.Name -ne $name -or $identity.Version.ToString() -ne $expectedAssemblyVersion) { throw ('Assembly identity mismatch: ' + $name) }
    if ($file.VersionInfo.FileVersion -ne ($manifest.version + '.0') -or $file.VersionInfo.FileDescription -ne $configuration.Publish.DisplayName.Value) { throw ('File version/title mismatch: ' + $name) }
}
$mcp = @(Get-ChildItem -LiteralPath $content -Recurse -File | Where-Object { $_.Name -match 'MCP|connection|token|\.cok$|\.pdb$' })
if ($mcp.Count -gt 0) { throw 'Unexpected bridge, private state, save or debug payload.' }
[pscustomobject]@{publication_verified=$true;version=$manifest.version;assets=$manifest.asset_count;files=$manifest.files.Count;access=$configuration.Publish.AccessLevel.Value;native_subscription_verified=($manifest.validation.native_subscription -eq 'passed')}
