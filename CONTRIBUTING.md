# Contributing to CodexUsageBar

CodexUsageBar is a Windows 11 taskbar widget. Contributions should preserve a
quiet, non-blocking taskbar experience and keep changes focused on one user
goal per pull request.

## Requirements

Use Windows 11 and the SDK selected by `global.json` (currently .NET 8.0.423).
Do not commit `dist/`, `artifacts/`, logs, or captured credentials. Keep raw
Codex data and account information out of issues, tests, fixtures, and
screenshots.

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore CodexUsageBar.sln
dotnet build CodexUsageBar.sln --configuration Release --no-restore
dotnet test CodexUsageBar.sln --configuration Release --no-build --verbosity minimal
powershell -NoProfile -Command "Invoke-Pester -Path '.\tests\PublishSupport.Tests.ps1'"
powershell -NoProfile -Command "Invoke-Pester -Path '.\tests\PublishScript.Tests.ps1'"
```

For a local interactive launch, run `run.bat` from the repository root.

## Pull requests

Explain the user impact, the tests you ran, and anything you could not verify.
Keep each pull request focused on one goal. UI changes need rendered checks at
100%, 150%, and 200% display scaling. Taskbar lifecycle changes need a real
Windows smoke test, including the affected taskbar behavior.
