$projectRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $projectRoot 'scripts/PublishSupport.psm1'
. (Join-Path $PSScriptRoot 'PublishTestSupport.ps1')
Import-Module $modulePath -Force

function New-TestArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string[]]$Entries
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open(
        $ArchivePath,
        [System.IO.Compression.ZipArchiveMode]::Create
    )
    try {
        foreach ($entry in $Entries) {
            $archive.CreateEntry($entry) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
}

Describe 'Get-ProjectVersion' {
    BeforeEach {
        $testRoot = Join-Path $TestDrive 'version-source'
        New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
        $propsPath = Join-Path $testRoot 'Directory.Build.props'
        Remove-Item -LiteralPath $propsPath -Force -ErrorAction SilentlyContinue
    }

    It 'returns the one semantic VersionPrefix' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value @'
<Project><PropertyGroup><VersionPrefix>1.1.3</VersionPrefix></PropertyGroup></Project>
'@
        Get-ProjectVersion -PropsPath $propsPath | Should Be '1.1.3'
    }

    It 'rejects a missing version source file' {
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'Version source is missing'
    }

    It 'rejects a missing VersionPrefix' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value '<Project><PropertyGroup /></Project>'
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'exactly one VersionPrefix'
    }

    It 'rejects duplicate VersionPrefix values' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value @'
<Project>
  <PropertyGroup><VersionPrefix>1.1.3</VersionPrefix></PropertyGroup>
  <PropertyGroup><VersionPrefix>1.1.4</VersionPrefix></PropertyGroup>
</Project>
'@
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'exactly one VersionPrefix'
    }

    It 'rejects one populated and one empty VersionPrefix node' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value @'
<Project>
  <PropertyGroup><VersionPrefix>1.1.3</VersionPrefix></PropertyGroup>
  <PropertyGroup><VersionPrefix>   </VersionPrefix></PropertyGroup>
</Project>
'@
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'exactly one VersionPrefix'
    }

    It 'rejects one empty VersionPrefix node as empty' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value @'
<Project><PropertyGroup><VersionPrefix>   </VersionPrefix></PropertyGroup></Project>
'@
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'VersionPrefix must not be empty'
    }

    It 'rejects a non-semantic VersionPrefix' {
        Set-Content -LiteralPath $propsPath -Encoding UTF8 -Value @'
<Project><PropertyGroup><VersionPrefix>1.1</VersionPrefix></PropertyGroup></Project>
'@
        { Get-ProjectVersion -PropsPath $propsPath } | Should Throw 'major.minor.patch'
    }
}

Describe 'Assert-ExecutableVersion' {
    It 'rejects a real PE without version resources' {
        $executable = Join-Path $TestDrive 'versionless.exe'
        New-VersionlessTestExecutable -Path $executable
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)

        [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($executable), 0, 2) |
            Should BeExactly 'MZ'
        [string]::IsNullOrWhiteSpace($versionInfo.ProductVersion) | Should Be $true
        [string]::IsNullOrWhiteSpace($versionInfo.FileVersion) | Should Be $true
        {
            Assert-ExecutableVersion `
                -Path $executable `
                -ExpectedProductVersion '9.9.5' `
                -ExpectedFileVersion '9.9.5.0'
        } | Should Throw 'ProductVersion is missing'
    }

    It 'rejects a real PE with the wrong ProductVersion' {
        $executable = Join-Path $TestDrive 'wrong-product.exe'
        New-VersionedTestExecutable `
            -Path $executable `
            -ProductVersion '9.9.4' `
            -FileVersion '9.9.5.0'
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)

        $versionInfo.ProductVersion | Should BeExactly '9.9.4'
        $versionInfo.FileVersion | Should BeExactly '9.9.5.0'
        {
            Assert-ExecutableVersion `
                -Path $executable `
                -ExpectedProductVersion '9.9.5' `
                -ExpectedFileVersion '9.9.5.0'
        } | Should Throw 'ProductVersion mismatch'
    }

    It 'rejects a real PE with the wrong FileVersion' {
        $executable = Join-Path $TestDrive 'wrong-file.exe'
        New-VersionedTestExecutable `
            -Path $executable `
            -ProductVersion '9.9.5' `
            -FileVersion '9.9.4.0'
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)

        $versionInfo.ProductVersion | Should BeExactly '9.9.5'
        $versionInfo.FileVersion | Should BeExactly '9.9.4.0'
        {
            Assert-ExecutableVersion `
                -Path $executable `
                -ExpectedProductVersion '9.9.5' `
                -ExpectedFileVersion '9.9.5.0'
        } | Should Throw 'FileVersion mismatch'
    }

    It 'accepts a real PE with both exact version values' {
        $executable = Join-Path $TestDrive 'matching.exe'
        New-VersionedTestExecutable `
            -Path $executable `
            -ProductVersion '9.9.5' `
            -FileVersion '9.9.5.0'
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)

        $versionInfo.ProductVersion | Should BeExactly '9.9.5'
        $versionInfo.FileVersion | Should BeExactly '9.9.5.0'
        {
            Assert-ExecutableVersion `
                -Path $executable `
                -ExpectedProductVersion '9.9.5' `
                -ExpectedFileVersion '9.9.5.0'
        } | Should Not Throw
    }
}

Describe 'Assert-RequiredReleaseInputs' {
    It 'rejects a missing required file' {
        {
            Assert-RequiredReleaseInputs -Paths @{
                Project = (Join-Path $TestDrive 'missing.csproj')
                Readme = (Join-Path $TestDrive 'missing-readme.md')
                Changelog = (Join-Path $TestDrive 'missing-changelog.md')
            }
        } | Should Throw 'Required release input is missing'
    }

    It 'rejects a required input that is a directory' {
        $directoryPath = Join-Path $TestDrive 'not-a-file'
        New-Item -ItemType Directory -Path $directoryPath | Out-Null

        {
            Assert-RequiredReleaseInputs -Paths @{
                Project = $directoryPath
            }
        } | Should Throw 'Required release input is missing'
    }
}

Describe 'release artifact helpers' {
    It 'rejects nested publish output' {
        $publishRoot = Join-Path $TestDrive 'publish'
        New-Item -ItemType Directory -Path (Join-Path $publishRoot 'nested') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $publishRoot 'CodexUsageBar.App.exe') -Value 'exe'
        Set-Content -LiteralPath (Join-Path $publishRoot 'nested/unexpected.txt') -Value 'unexpected'

        {
            Assert-PublishOutput -PublishDirectory $publishRoot
        } | Should Throw 'Publish output must contain only'
    }

    It 'creates a zip with exactly the requested root files' {
        $packageRoot = Join-Path $TestDrive 'package'
        $archivePath = Join-Path $TestDrive 'CodexUsageBar_1.1.3_win-x64.zip'
        New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
        'exe' | Set-Content -LiteralPath (Join-Path $packageRoot 'CodexUsageBar.exe')
        'readme' | Set-Content -LiteralPath (Join-Path $packageRoot 'README.md')
        'changes' | Set-Content -LiteralPath (Join-Path $packageRoot 'CHANGELOG.md')
        'license' | Set-Content -LiteralPath (Join-Path $packageRoot 'LICENSE')
        'notices' | Set-Content -LiteralPath (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.txt')

        New-ReleaseArchive -SourceDirectory $packageRoot -ArchivePath $archivePath
        Assert-ReleaseArchive -ArchivePath $archivePath -ExpectedEntries @(
            'CHANGELOG.md',
            'CodexUsageBar.exe',
            'LICENSE',
            'README.md',
            'THIRD-PARTY-NOTICES.txt'
        )

        Test-Path -LiteralPath $archivePath -PathType Leaf | Should Be $true
    }

    It 'rejects a release archive entry with incorrect casing' {
        $archivePath = Join-Path $TestDrive 'wrong-case.zip'
        New-TestArchive -ArchivePath $archivePath -Entries @(
            'CHANGELOG.md',
            'codexusagebar.exe',
            'LICENSE',
            'README.md'
            'THIRD-PARTY-NOTICES.txt'
        )

        {
            Assert-ReleaseArchive -ArchivePath $archivePath -ExpectedEntries @(
                'CHANGELOG.md',
                'CodexUsageBar.exe',
                'LICENSE',
                'README.md',
                'THIRD-PARTY-NOTICES.txt'
            )
        } | Should Throw 'Release archive entries are invalid'
    }

    It 'rejects an additional root release archive entry' {
        $archivePath = Join-Path $TestDrive 'extra-root-entry.zip'
        New-TestArchive -ArchivePath $archivePath -Entries @(
            'CHANGELOG.md',
            'CodexUsageBar.exe',
            'LICENSE',
            'README.md',
            'THIRD-PARTY-NOTICES.txt',
            'unexpected.txt'
        )

        {
            Assert-ReleaseArchive -ArchivePath $archivePath -ExpectedEntries @(
                'CHANGELOG.md',
                'CodexUsageBar.exe',
                'LICENSE',
                'README.md',
                'THIRD-PARTY-NOTICES.txt'
            )
        } | Should Throw 'Release archive entries are invalid'
    }

    It 'rejects a nested release archive entry' {
        $archivePath = Join-Path $TestDrive 'nested-entry.zip'
        New-TestArchive -ArchivePath $archivePath -Entries @(
            'CHANGELOG.md',
            'CodexUsageBar.exe',
            'LICENSE',
            'README.md',
            'THIRD-PARTY-NOTICES.txt',
            'nested/unexpected.txt'
        )

        {
            Assert-ReleaseArchive -ArchivePath $archivePath -ExpectedEntries @(
                'CHANGELOG.md',
                'CodexUsageBar.exe',
                'LICENSE',
                'README.md',
                'THIRD-PARTY-NOTICES.txt'
            )
        } | Should Throw 'Release archive entries are invalid'
    }

    It 'writes sorted SHA-256 entries for the supplied files' {
        $first = Join-Path $TestDrive 'b.zip'
        $second = Join-Path $TestDrive 'a.exe'
        $manifest = Join-Path $TestDrive 'SHA256SUMS.txt'
        'zip' | Set-Content -LiteralPath $first -NoNewline
        'exe' | Set-Content -LiteralPath $second -NoNewline

        Write-ChecksumManifest -Paths @($first, $second) -Destination $manifest
        $lines = @(Get-Content -LiteralPath $manifest)
        $firstHash = (Get-FileHash -LiteralPath $first -Algorithm SHA256).Hash.ToUpperInvariant()
        $secondHash = (Get-FileHash -LiteralPath $second -Algorithm SHA256).Hash.ToUpperInvariant()

        $lines.Count | Should Be 2
        $lines[0] | Should BeExactly "$secondHash  a.exe"
        $lines[1] | Should BeExactly "$firstHash  b.zip"
    }
}
