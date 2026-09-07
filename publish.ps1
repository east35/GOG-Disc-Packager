param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget-packages'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactRoot = if ($OutputDirectory) {
    if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        [IO.Path]::GetFullPath($OutputDirectory)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
    }
} else {
    Join-Path $projectRoot "artifacts\GOGDiscTool-$stamp"
}
$launcherOutput = Join-Path $artifactRoot 'LauncherPayload'
$packagerTemp = Join-Path $projectRoot "artifacts\.packager-$stamp"

try {
    New-Item -ItemType Directory -Path $launcherOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $packagerTemp -Force | Out-Null

    dotnet restore (Join-Path $projectRoot 'GOGDiscTool.sln') `
        --configfile (Join-Path $projectRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }

    $launcherArguments = @(
        'publish', (Join-Path $projectRoot 'src\GogDisc.Launcher\GogDisc.Launcher.csproj'),
        '-c', $Configuration, '--no-restore', '-o', $launcherOutput
    )
    if ($Version) { $launcherArguments += "-p:Version=$Version" }
    & dotnet @launcherArguments
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    Get-ChildItem -LiteralPath $launcherOutput -File -Filter '*.pdb' | Remove-Item -Force

    $packagerArguments = @(
        'publish', (Join-Path $projectRoot 'src\GogDisc.Packager\GogDisc.Packager.csproj'),
        '-c', $Configuration, '--no-restore', '-o', $packagerTemp
    )
    if ($Version) { $packagerArguments += "-p:Version=$Version" }
    & dotnet @packagerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Packager publish failed.' }

    Copy-Item -LiteralPath (Join-Path $packagerTemp 'GOG Disc Packager.exe') -Destination $artifactRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $artifactRoot

    Write-Host "Published GOG Disc Tool to:"
    Write-Host $artifactRoot
    Write-Host "Run 'GOG Disc Packager.exe' from that folder."
}
catch {
    if (Test-Path -LiteralPath $artifactRoot) { Remove-Item -LiteralPath $artifactRoot -Recurse -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $packagerTemp) { Remove-Item -LiteralPath $packagerTemp -Recurse -Force }
}
