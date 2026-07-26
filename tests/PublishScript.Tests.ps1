$sourceProjectRoot = Split-Path -Parent $PSScriptRoot
$sourceScript = Join-Path $sourceProjectRoot 'scripts/publish.ps1'
$sourceModule = Join-Path $sourceProjectRoot 'scripts/PublishSupport.psm1'
. (Join-Path $PSScriptRoot 'PublishTestSupport.ps1')

function New-PublishFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $fixtureRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
    $scriptsRoot = Join-Path $fixtureRoot 'scripts'
    $projectDirectory = Join-Path $fixtureRoot 'src/CodexUsageBar.App'
    New-Item -ItemType Directory -Path $scriptsRoot, $projectDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceScript -Destination (Join-Path $scriptsRoot 'publish.ps1')
    Copy-Item -LiteralPath $sourceModule -Destination (Join-Path $scriptsRoot 'PublishSupport.psm1')
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Directory.Build.props') -Encoding UTF8 -Value @"
<Project><PropertyGroup><VersionPrefix>$Version</VersionPrefix></PropertyGroup></Project>
"@
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Encoding UTF8 -Value '# Fixture'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'CHANGELOG.md') -Encoding UTF8 -Value @"
# Changelog

## [Unreleased]

## [$Version] - 2026-07-26
"@
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'LICENSE') -Encoding UTF8 -Value 'MIT fixture'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'THIRD-PARTY-NOTICES.txt') -Encoding UTF8 -Value 'Third-party fixture'
    Set-Content -LiteralPath (Join-Path $projectDirectory 'CodexUsageBar.App.csproj') -Encoding UTF8 -Value '<Project Sdk="Microsoft.NET.Sdk" />'

    return [pscustomobject]@{
        Root = $fixtureRoot
        Script = (Join-Path $scriptsRoot 'publish.ps1')
        Release = (Join-Path $fixtureRoot "dist/$Version")
        Staging = (Join-Path $fixtureRoot 'artifacts/publish-staging')
    }
}

function New-FakeDotnet {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Body
    )

    $bin = Join-Path $Root 'fake-bin'
    $command = Join-Path $bin 'dotnet.cmd'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    Set-Content -LiteralPath $command -NoNewline -Value $Body
    return $bin
}

function New-FakeDotnetPublisher {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Executable
    )

    return New-FakeDotnet -Root $Root -Body @"
@echo off
setlocal
set "publishOutput="
:next
if "%~1"=="" goto done
if "%~1"=="-o" (
  set "publishOutput=%~2"
  shift
)
shift
goto next
:done
copy /y "$Executable" "%publishOutput%\CodexUsageBar.App.exe" > nul
exit /b 0
"@
}

Describe 'publish.ps1' {
    It 'rejects validation when the license is missing' {
        $fixture = New-PublishFixture -Version '9.9.0'
        Remove-Item -LiteralPath (Join-Path $fixture.Root 'LICENSE')

        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script -WhatIfValidation 2>&1

        $LASTEXITCODE | Should Not Be 0
        ($output | Out-String) | Should Match 'Required release input is missing'
        Test-Path -LiteralPath $fixture.Release | Should Be $false
    }

    It 'rejects validation when third-party notices are missing' {
        $fixture = New-PublishFixture -Version '9.8.9'
        Remove-Item -LiteralPath (Join-Path $fixture.Root 'THIRD-PARTY-NOTICES.txt')

        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script -WhatIfValidation 2>&1

        $LASTEXITCODE | Should Not Be 0
        ($output | Out-String) | Should Match 'Required release input is missing'
        Test-Path -LiteralPath $fixture.Release | Should Be $false
    }

    It 'rejects validation when the changelog version is behind the version source' {
        $fixture = New-PublishFixture -Version '9.9.7'
        Set-Content -LiteralPath (Join-Path $fixture.Root 'CHANGELOG.md') -Encoding UTF8 -Value @'
# Changelog

## [Unreleased]

## [9.8.9] - 2026-07-26
'@

        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script -WhatIfValidation 2>&1

        $LASTEXITCODE | Should Not Be 0
        ($output | Out-String) | Should Match 'Version source and changelog are out of sync'
        Test-Path -LiteralPath $fixture.Release | Should Be $false
    }

    It 'refuses to validate a release version that already exists' {
        $fixture = New-PublishFixture -Version '9.9.9'
        New-Item -ItemType Directory -Path $fixture.Release | Out-Null

        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script -WhatIfValidation 2>&1

        $LASTEXITCODE | Should Not Be 0
        ($output | Out-String) | Should Match 'Release directory already exists'
    }

    It 'rejects nested files even when the allowed executable is present' {
        $fixture = New-PublishFixture -Version '9.9.8'
        $sentinel = Join-Path $fixture.Staging 'sibling-sentinel'
        $sentinelMarker = Join-Path $sentinel 'keep.txt'
        $originalPath = $env:Path
        New-Item -ItemType Directory -Path $sentinel -Force | Out-Null
        Set-Content -LiteralPath $sentinelMarker -Value 'keep'
        $fakeBin = New-FakeDotnet -Root $fixture.Root -Body @'
@echo off
setlocal
set "publishOutput="
:next
if "%~1"=="" goto done
if "%~1"=="-o" (
  set "publishOutput=%~2"
  shift
)
shift
goto next
:done
mkdir "%publishOutput%\nested"
type nul > "%publishOutput%\CodexUsageBar.App.exe"
type nul > "%publishOutput%\nested\unexpected.txt"
exit /b 0
'@

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>&1

            $LASTEXITCODE | Should Not Be 0
            ($output | Out-String) | Should Match 'Publish output must contain only'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
            Test-Path -LiteralPath $sentinelMarker -PathType Leaf | Should Be $true
            $remainingStagingEntries = @(Get-ChildItem -LiteralPath $fixture.Staging -Force)
            $remainingStagingEntries.Count | Should Be 1
            $remainingStagingEntries[0].FullName | Should Be $sentinel
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'rejects a directory named like the required executable' {
        $fixture = New-PublishFixture -Version '9.9.7'
        $sentinel = Join-Path $fixture.Staging 'sibling-sentinel'
        $sentinelMarker = Join-Path $sentinel 'keep.txt'
        $originalPath = $env:Path
        New-Item -ItemType Directory -Path $sentinel -Force | Out-Null
        Set-Content -LiteralPath $sentinelMarker -Value 'keep'
        $fakeBin = New-FakeDotnet -Root $fixture.Root -Body @'
@echo off
setlocal
set "publishOutput="
:next
if "%~1"=="" goto done
if "%~1"=="-o" (
  set "publishOutput=%~2"
  shift
)
shift
goto next
:done
mkdir "%publishOutput%\CodexUsageBar.App.exe"
exit /b 0
'@

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>&1

            $LASTEXITCODE | Should Not Be 0
            ($output | Out-String) | Should Match 'Publish output must contain only'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
            Test-Path -LiteralPath $sentinelMarker -PathType Leaf | Should Be $true
            $remainingStagingEntries = @(Get-ChildItem -LiteralPath $fixture.Staging -Force)
            $remainingStagingEntries.Count | Should Be 1
            $remainingStagingEntries[0].FullName | Should Be $sentinel
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'validates a new release without invoking dotnet or creating output' {
        $fixture = New-PublishFixture -Version '9.9.6'
        $dotnetMarker = Join-Path $fixture.Root 'dotnet-was-called.txt'
        $originalPath = $env:Path
        $fakeBin = New-FakeDotnet -Root $fixture.Root -Body "@echo off`r`necho called > `"$dotnetMarker`"`r`nexit /b 94`r`n"

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script -WhatIfValidation 2>&1

            $LASTEXITCODE | Should Be 0
            ($output | Out-String) | Should Match 'Release validation passed'
            (Test-Path -LiteralPath $dotnetMarker) | Should Be $false
            (Test-Path -LiteralPath $fixture.Release) | Should Be $false
            (Test-Path -LiteralPath $fixture.Staging) | Should Be $false
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'disables source revision suffixes in the exact dotnet publish arguments' {
        $fixture = New-PublishFixture -Version '9.9.4'
        $argumentMarker = Join-Path $fixture.Root 'dotnet-arguments.txt'
        $originalPath = $env:Path
        $fakeBin = New-FakeDotnet -Root $fixture.Root -Body (
            "@echo off`r`necho %* > `"$argumentMarker`"`r`nexit /b 93`r`n"
        )

        try {
            $env:Path = "$fakeBin;$originalPath"
            & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>$null

            $LASTEXITCODE | Should Be 1
            Test-Path -LiteralPath $argumentMarker -PathType Leaf | Should Be $true
            $arguments = @(
                (Get-Content -LiteralPath $argumentMarker -Raw).Trim() -split '\s+'
            )
            $sourceRevisionArguments = @(
                $arguments |
                    Where-Object {
                        $_ -like '-p:IncludeSourceRevisionInInformationalVersion=*'
                    }
            )

            $sourceRevisionArguments.Count | Should Be 1
            $sourceRevisionArguments[0] |
                Should BeExactly '-p:IncludeSourceRevisionInInformationalVersion=false'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
            @(Get-ChildItem -LiteralPath $fixture.Staging -Force -ErrorAction SilentlyContinue).Count |
                Should Be 0
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'rejects a published PE without version resources before moving the release' {
        $fixture = New-PublishFixture -Version '9.9.3'
        $candidate = Join-Path $fixture.Root 'versionless.exe'
        $originalPath = $env:Path
        New-VersionlessTestExecutable -Path $candidate
        $fakeBin = New-FakeDotnetPublisher -Root $fixture.Root -Executable $candidate

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>&1

            $LASTEXITCODE | Should Not Be 0
            ($output | Out-String) | Should Match 'ProductVersion is missing'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'rejects a published PE with the wrong ProductVersion before moving the release' {
        $fixture = New-PublishFixture -Version '9.9.2'
        $candidate = Join-Path $fixture.Root 'wrong-product.exe'
        $originalPath = $env:Path
        New-VersionedTestExecutable `
            -Path $candidate `
            -ProductVersion '9.9.1' `
            -FileVersion '9.9.2.0'
        $fakeBin = New-FakeDotnetPublisher -Root $fixture.Root -Executable $candidate

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>&1

            $LASTEXITCODE | Should Not Be 0
            ($output | Out-String) | Should Match 'ProductVersion mismatch'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'rejects a published PE with the wrong FileVersion before moving the release' {
        $fixture = New-PublishFixture -Version '9.9.1'
        $candidate = Join-Path $fixture.Root 'wrong-file.exe'
        $originalPath = $env:Path
        New-VersionedTestExecutable `
            -Path $candidate `
            -ProductVersion '9.9.1' `
            -FileVersion '9.9.0.0'
        $fakeBin = New-FakeDotnetPublisher -Root $fixture.Root -Executable $candidate

        try {
            $env:Path = "$fakeBin;$originalPath"
            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script 2>&1

            $LASTEXITCODE | Should Not Be 0
            ($output | Out-String) | Should Match 'FileVersion mismatch'
            Test-Path -LiteralPath $fixture.Release | Should Be $false
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'publishes through staging and moves a complete release directory once' {
        $fixture = New-PublishFixture -Version '9.9.5'
        $candidate = Join-Path $fixture.Root 'matching.exe'
        $originalPath = $env:Path
        New-VersionedTestExecutable `
            -Path $candidate `
            -ProductVersion '9.9.5' `
            -FileVersion '9.9.5.0'
        $fakeBin = New-FakeDotnetPublisher -Root $fixture.Root -Executable $candidate

        try {
            $env:Path = "$fakeBin;$originalPath"
            & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script
            $LASTEXITCODE | Should Be 0
            Test-Path -LiteralPath (Join-Path $fixture.Release 'CodexUsageBar.exe') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.Release 'CodexUsageBar_9.9.5_win-x64.zip') -PathType Leaf | Should Be $true
            Test-Path -LiteralPath (Join-Path $fixture.Release 'SHA256SUMS.txt') -PathType Leaf | Should Be $true
            @(Get-ChildItem -LiteralPath $fixture.Staging -Force -ErrorAction SilentlyContinue).Count | Should Be 0
        }
        finally {
            $env:Path = $originalPath
        }
    }
}

Describe 'release documentation and local build contracts' {
    It 'defines a pinned Windows CI workflow through the final release candidate' {
        $workflowPath = Join-Path $sourceProjectRoot '.github/workflows/ci.yml'
        Test-Path -LiteralPath $workflowPath -PathType Leaf | Should Be $true

        $workflow = Get-Content -LiteralPath $workflowPath -Raw
        $workflow | Should Match 'windows-latest'
        $workflow | Should Match 'actions/checkout@[0-9a-f]{40}\s+# v7'
        $workflow | Should Match 'actions/setup-dotnet@[0-9a-f]{40}\s+# v6'
        $workflow | Should Match 'actions/upload-artifact@[0-9a-f]{40}\s+# v7'
        $workflow | Should Match 'dotnet restore CodexUsageBar\.sln'
        $workflow | Should Match 'dotnet build CodexUsageBar\.sln --configuration Release --no-restore'
        $workflow | Should Match 'dotnet test CodexUsageBar\.sln --configuration Release --no-build'
        $workflow | Should Match 'PublishSupport\.Tests\.ps1'
        $workflow | Should Match 'PublishScript\.Tests\.ps1'
        $workflow | Should Match 'Build release candidate'
        $workflow | Should Match '\.\\scripts\\publish\.ps1'
        $workflow | Should Match 'dist/\*/\*_win-x64\.zip'
    }

    It 'preserves the WPF assembly identity in the exact dotnet publish arguments' {
        $fixture = New-PublishFixture -Version '9.9.3'
        $capturePath = Join-Path $fixture.Root 'dotnet-arguments.txt'
        $fakeDotnet = New-FakeDotnet -Root $fixture.Root -Body @"
@echo off
> "$capturePath" echo %*
exit /b 37
"@
        $originalPath = $env:Path

        try {
            $env:Path = "$fakeDotnet;$originalPath"

            & powershell -NoProfile -ExecutionPolicy Bypass -File $fixture.Script *> $null

            $LASTEXITCODE | Should Be 1
            $arguments = Get-Content -LiteralPath $capturePath -Raw
            ([regex]::Matches($arguments, '(?i)-p:AssemblyName=')).Count | Should Be 0
            $arguments | Should Not Match '(?i)(?:^|\s)-p:AssemblyName='
            Test-Path -LiteralPath $fixture.Release | Should Be $false
            @(Get-ChildItem -LiteralPath $fixture.Staging -Force -ErrorAction SilentlyContinue).Count |
                Should Be 0
        }
        finally {
            $env:Path = $originalPath
        }
    }

    It 'keeps build.bat to one ordinary Release build command' {
        $content = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'build.bat') -Raw
        $content | Should Match 'dotnet build'
        $content | Should Match '--configuration Release'
        $content | Should Not Match 'dotnet publish'
        $content | Should Not Match 'dist'
        $content | Should Not Match 'Compress-Archive'
        $content | Should Not Match 'VERSION='

        $commandLines = @(
            $content -split "`r?`n" |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ -and $_ -notmatch '^(?i:rem\b|::)' }
        )
        $dotnetCommands = @($commandLines | Where-Object { $_ -match '^(?i:dotnet)\s+' })

        $dotnetCommands.Count | Should Be 1
        $dotnetCommands[0] | Should Match '^(?i:dotnet)\s+build\s+'
        $dotnetCommands[0] | Should Match '(?i:--configuration\s+Release)'
    }

    It 'keeps the open-source entry documents complete and executable' {
        $requiredFiles = @(
            'LICENSE',
            'THIRD-PARTY-NOTICES.txt',
            'CONTRIBUTING.md',
            'SECURITY.md',
            'CODE_OF_CONDUCT.md',
            '.github/PULL_REQUEST_TEMPLATE.md',
            '.github/ISSUE_TEMPLATE/bug_report.yml',
            '.github/ISSUE_TEMPLATE/feature_request.yml',
            '.github/ISSUE_TEMPLATE/config.yml',
            'docs/architecture.md',
            'docs/privacy.md',
            'docs/release.md'
        )

        foreach ($relativePath in $requiredFiles) {
            Test-Path -LiteralPath (Join-Path $sourceProjectRoot $relativePath) -PathType Leaf |
                Should Be $true
        }

        $readme = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'README.md') -Raw
        $readme | Should Match 'GitHub Releases'
        $readme | Should Match 'https://github\.com/puwenfu/CodexUsageBar/releases/latest'
        $readme | Should Match '(?m)^Get-FileHash \.\\CodexUsageBar_\*_win-x64\.zip -Algorithm SHA256\s*$'
        $readme | Should Match 'dotnet restore CodexUsageBar\.sln'
        $readme | Should Match 'SHA256SUMS\.txt'
        $readme | Should Match '(?i)not affiliated with or endorsed by OpenAI'
        $readme | Should Match '(?i)unsigned'
        $readme | Should Match '(?is)deterministic WPF rendering at 150% DPI with sample values.*not\s+a live Windows Shell screenshot'
    }

    It 'documents the parameterless release command and release contents' {
        $readme = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'README.md') -Raw
        $publishScript = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'scripts/publish.ps1') -Raw

        $readme | Should Match '(?m)^powershell -ExecutionPolicy Bypass -File \.\\scripts\\publish\.ps1\s*$'
        $readme | Should Match '(?m)^powershell -ExecutionPolicy Bypass -File \.\\scripts\\publish\.ps1 -WhatIfValidation\s*$'
        $readme | Should Match '(?i)standalone EXE'
        $readme | Should Match '(?is)ZIP.*README\.md.*CHANGELOG\.md.*LICENSE.*THIRD-PARTY-NOTICES\.txt'
        $readme | Should Match 'SHA256SUMS\.txt'
        $publishScript | Should Match '-p:EnableCompressionInSingleFile=true'
    }

    It 'keeps the project version aligned with the latest changelog release' {
        [xml]$props = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'Directory.Build.props') -Raw
        $changelog = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'CHANGELOG.md') -Raw
        $versionNodes = @($props.SelectNodes('//VersionPrefix'))
        $releasedHeadings = [regex]::Matches(
            $changelog,
            '(?m)^## \[(?!Unreleased\])([^\]]+)\]')

        $versionNodes.Count | Should Be 1
        $versionNodes[0].InnerText | Should Be '1.2.5'
        $releasedHeadings.Count | Should BeGreaterThan 0
        $releasedHeadings[0].Groups[1].Value |
            Should BeExactly $versionNodes[0].InnerText
        $changelog | Should Match '\[1\.2\.5\]\s+-\s+2026-07-26'
        $changelog | Should Match '(?i)refresh animation'
        $changelog | Should Match '(?i)nested menus'
        $changelog | Should Match '(?i)debug panel'
        $changelog | Should Match '(?i)GitHub Actions'
    }

    It 'keeps the 1.2.4 release notes aligned with the published package' {
        $changelog = Get-Content -LiteralPath (Join-Path $sourceProjectRoot 'CHANGELOG.md') -Raw
        $releaseNotesPath = Join-Path $sourceProjectRoot 'docs/releases/1.2.4.md'

        Test-Path -LiteralPath $releaseNotesPath -PathType Leaf | Should Be $true
        $releaseNotes = Get-Content -LiteralPath $releaseNotesPath -Raw

        $changelog | Should Match '(?i)Windows 11'
        $changelog | Should Match '(?i)\.NET 8'
        $releaseNotes | Should Match '(?m)^# CodexUsageBar 1\.2\.4\s*$'
        $releaseNotes | Should Match 'CodexUsageBar_1\.2\.4_win-x64\.zip'
        $releaseNotes | Should Match '(?m)^`[A-F0-9]{64}`\s*$'
        $releaseNotes | Should Match '(?is)release ZIP.*SHA256SUMS\.txt.*authorized GitHub Release'
        $releaseNotes | Should Not Match '(?i)not available yet|after an authorized package build'
        $releaseNotes | Should Match '(?i)Windows 11'
        $releaseNotes | Should Match '(?i)self-contained'
        $releaseNotes | Should Match '(?i)does not require a separately installed \.NET runtime'
    }
}
