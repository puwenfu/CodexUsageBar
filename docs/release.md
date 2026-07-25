# Release process

The public repository at `puwenfu/CodexUsageBar` is the authoritative source
for release branches, tags, and GitHub Releases. Private archive remotes are
backup-only and must never be used as the target of a public release.

## Version source

`Directory.Build.props` is the authoritative version source. The release script
derives executable metadata, the release directory, archive name, and checksum
manifest from that version.

## Ordinary build

Use `build.bat` for a normal Release build. It builds the solution and does not
create or publish a release archive.

## Official release command

Run the following command only when local packaging has been approved:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The validation-only form checks release inputs without invoking publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -WhatIfValidation
```

## Immutable release contract

The script refuses to overwrite an existing version directory. It publishes to
staging, validates the standalone EXE and its version resources, then moves the
complete release directory into place once. The release ZIP contains the
standalone EXE, `README.md`, `CHANGELOG.md`, `LICENSE`, and
`THIRD-PARTY-NOTICES.txt`; `SHA256SUMS.txt` records SHA-256 hashes for the EXE
and ZIP. Verify the final ZIP contents, hashes, and the real EXE launch before
sharing a release.
