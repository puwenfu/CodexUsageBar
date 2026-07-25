Set-StrictMode -Version Latest

function Get-ProjectVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PropsPath
    )

    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) {
        throw "Version source is missing: $PropsPath"
    }

    [xml]$document = Get-Content -LiteralPath $PropsPath -Raw -Encoding UTF8
    $nodes = @($document.SelectNodes('//VersionPrefix'))

    if ($nodes.Count -ne 1) {
        throw "Version source must contain exactly one VersionPrefix: $PropsPath"
    }

    $value = $nodes[0].InnerText.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "VersionPrefix must not be empty: $PropsPath"
    }

    if ($value -notmatch '^\d+\.\d+\.\d+$') {
        throw "VersionPrefix must use major.minor.patch: $value"
    }

    return $value
}

function Assert-ExecutableVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedProductVersion,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedFileVersion
    )

    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        throw "Published executable ProductVersion is missing: $Path"
    }
    if ($versionInfo.ProductVersion -cne $ExpectedProductVersion) {
        throw "Published executable ProductVersion mismatch. Expected $ExpectedProductVersion; found $($versionInfo.ProductVersion)."
    }

    if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        throw "Published executable FileVersion is missing: $Path"
    }
    if ($versionInfo.FileVersion -cne $ExpectedFileVersion) {
        throw "Published executable FileVersion mismatch. Expected $ExpectedFileVersion; found $($versionInfo.FileVersion)."
    }
}

function Assert-RequiredReleaseInputs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Paths
    )

    foreach ($entry in $Paths.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
            throw "Required release input is missing: $($entry.Key): $($entry.Value)"
        }
    }
}

function Assert-PublishOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $expected = @('CodexUsageBar.App.exe')

    $entries = @(Get-ChildItem -LiteralPath $PublishDirectory -Force -Recurse)
    $actual = @($entries | ForEach-Object {
        [pscustomobject]@{
            RelativePath = $_.FullName.Substring($PublishDirectory.Length).TrimStart(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar
            )
            IsFile = $_ -is [System.IO.FileInfo]
        }
    })

    $invalid = @($actual | Where-Object {
        -not $_.IsFile -or $_.RelativePath -notin $expected
    })
    $missing = @($expected | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $PublishDirectory $_) -PathType Leaf)
    })

    if ($actual.Count -ne $expected.Count -or $invalid.Count -ne 0 -or $missing.Count -ne 0) {
        throw "Publish output must contain only $($expected -join ', ')."
    }
}

function New-ReleaseArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath
    )

    if (Test-Path -LiteralPath $ArchivePath) {
        throw "Archive path already exists: $ArchivePath"
    }

    Compress-Archive -LiteralPath @(
        (Join-Path $SourceDirectory 'CodexUsageBar.exe'),
        (Join-Path $SourceDirectory 'README.md'),
        (Join-Path $SourceDirectory 'CHANGELOG.md'),
        (Join-Path $SourceDirectory 'LICENSE'),
        (Join-Path $SourceDirectory 'THIRD-PARTY-NOTICES.txt')
    ) -DestinationPath $ArchivePath -CompressionLevel Optimal
}

function Assert-ReleaseArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedEntries
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $actual = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object -CaseSensitive)
        $expected = @($ExpectedEntries | Sort-Object -CaseSensitive)
        if (($actual -join "`n") -cne ($expected -join "`n")) {
            throw "Release archive entries are invalid. Expected: $($expected -join ', '); actual: $($actual -join ', ')"
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Write-ChecksumManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $lines = @(
        $Paths |
            Sort-Object { Split-Path -Leaf $_ } |
            ForEach-Object {
                $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
                "$($hash.Hash.ToUpperInvariant())  $(Split-Path -Leaf $_)"
            }
    )
    Set-Content -LiteralPath $Destination -Value $lines -Encoding ASCII
}

Export-ModuleMember -Function @(
    'Get-ProjectVersion',
    'Assert-ExecutableVersion',
    'Assert-RequiredReleaseInputs',
    'Assert-PublishOutput',
    'New-ReleaseArchive',
    'Assert-ReleaseArchive',
    'Write-ChecksumManifest'
)
