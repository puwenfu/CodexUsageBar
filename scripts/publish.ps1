[CmdletBinding()]
param(
    [switch]$WhatIfValidation
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$supportModule = Join-Path $PSScriptRoot 'PublishSupport.psm1'
Import-Module $supportModule -Force

$version = Get-ProjectVersion -PropsPath (Join-Path $projectRoot 'Directory.Build.props')
$projectFile = Join-Path $projectRoot 'src/CodexUsageBar.App/CodexUsageBar.App.csproj'
$readmeFile = Join-Path $projectRoot 'README.md'
$changelogFile = Join-Path $projectRoot 'CHANGELOG.md'
$licenseFile = Join-Path $projectRoot 'LICENSE'
$thirdPartyNoticesFile = Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt'
$releaseRoot = Join-Path $projectRoot "dist/$version"

Assert-RequiredReleaseInputs -Paths @{
    Project = $projectFile
    Readme = $readmeFile
    Changelog = $changelogFile
    License = $licenseFile
    ThirdPartyNotices = $thirdPartyNoticesFile
}
Assert-ChangelogVersion -ChangelogPath $changelogFile -ExpectedVersion $version

if (Test-Path -LiteralPath $releaseRoot) {
    throw "Release directory already exists: $releaseRoot"
}

if ($WhatIfValidation) {
    Write-Output "Release validation passed for CodexUsageBar $version`: $releaseRoot"
    exit 0
}

$distRoot = Join-Path $projectRoot 'dist'
$stagingParent = Join-Path $projectRoot 'artifacts/publish-staging'
$runRoot = Join-Path $stagingParent ([Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $runRoot 'publish'
$packageDirectory = Join-Path $runRoot 'package'
$stagedRelease = Join-Path $runRoot $version
$archiveName = "CodexUsageBar_${version}_win-x64.zip"
$stagedExe = Join-Path $stagedRelease 'CodexUsageBar.exe'
$stagedArchive = Join-Path $stagedRelease $archiveName
$stagedManifest = Join-Path $stagedRelease 'SHA256SUMS.txt'

try {
    New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory, $stagedRelease -Force | Out-Null

    $publishArguments = @(
        'publish',
        $projectFile,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        "-p:Version=$version",
        "-p:FileVersion=$version.0",
        "-p:AssemblyVersion=$version.0",
        "-p:InformationalVersion=$version",
        '-p:IncludeSourceRevisionInInformationalVersion=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None',
        '-o', $publishDirectory
    )

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Assert-PublishOutput -PublishDirectory $publishDirectory

    $publishedExe = Join-Path $publishDirectory 'CodexUsageBar.App.exe'
    Assert-ExecutableVersion `
        -Path $publishedExe `
        -ExpectedProductVersion $version `
        -ExpectedFileVersion "$version.0"

    Copy-Item -LiteralPath $publishedExe -Destination $stagedExe
    Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $packageDirectory 'CodexUsageBar.exe')
    Copy-Item -LiteralPath $readmeFile -Destination (Join-Path $packageDirectory 'README.md')
    Copy-Item -LiteralPath $changelogFile -Destination (Join-Path $packageDirectory 'CHANGELOG.md')
    Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $packageDirectory 'LICENSE')
    Copy-Item -LiteralPath $thirdPartyNoticesFile -Destination (
        Join-Path $packageDirectory 'THIRD-PARTY-NOTICES.txt'
    )

    New-ReleaseArchive -SourceDirectory $packageDirectory -ArchivePath $stagedArchive
    Assert-ReleaseArchive -ArchivePath $stagedArchive -ExpectedEntries @(
        'CHANGELOG.md',
        'CodexUsageBar.exe',
        'LICENSE',
        'README.md',
        'THIRD-PARTY-NOTICES.txt'
    )
    Write-ChecksumManifest -Paths @($stagedExe, $stagedArchive) -Destination $stagedManifest

    if (Test-Path -LiteralPath $releaseRoot) {
        throw "Release directory already exists: $releaseRoot"
    }
    if (-not (Test-Path -LiteralPath $distRoot)) {
        New-Item -ItemType Directory -Path $distRoot | Out-Null
    }

    [System.IO.Directory]::Move($stagedRelease, $releaseRoot)
    Write-Output "Published CodexUsageBar $version to $releaseRoot"
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
