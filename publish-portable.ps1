[CmdletBinding()]
param()

$projectRoot = $PSScriptRoot
$version = '1.0.2'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputPath = Join-Path $projectRoot "artifacts\release-staging\$version-$stamp"
New-Item -ItemType Directory -Path $outputPath -ErrorAction Stop | Out-Null

dotnet publish (Join-Path $projectRoot 'NSCheatManager.Windows.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $outputPath

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$executable = Join-Path $outputPath 'NSCheatManager.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Portable executable was not created: $executable"
}

if (Test-Path -LiteralPath (Join-Path $outputPath 'data')) {
    throw 'Release staging unexpectedly contains runtime data. Refusing to package it.'
}

$expectedNames = @('LICENSE.txt', 'licenses', 'NSCheatManager.exe', 'THIRD-PARTY-NOTICES.txt')
$actualNames = @(Get-ChildItem -LiteralPath $outputPath -Force | Select-Object -ExpandProperty Name | Sort-Object)
if (($actualNames -join "`n") -ne (($expectedNames | Sort-Object) -join "`n")) {
    throw "Unexpected release-staging content: $($actualNames -join ', ')"
}

Write-Output "Clean portable staging: $outputPath"
