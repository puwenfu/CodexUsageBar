@echo off
setlocal
set "PROJECT_DIR=%~dp0"
set "CODEX_USAGE_BAR_PROJECT_DIR=%PROJECT_DIR%"
set "CODEX_USAGE_BAR_LOG=%PROJECT_DIR%artifacts\run.log"

if not exist "%PROJECT_DIR%artifacts" mkdir "%PROJECT_DIR%artifacts"

powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -Command "$script = '& { Set-Location -LiteralPath $env:CODEX_USAGE_BAR_PROJECT_DIR; & dotnet run --project (Join-Path $env:CODEX_USAGE_BAR_PROJECT_DIR ''src\CodexUsageBar.App\CodexUsageBar.App.csproj'') --configuration Debug *> $env:CODEX_USAGE_BAR_LOG }'; $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script)); Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-WindowStyle','Hidden','-EncodedCommand',$encoded -WindowStyle Hidden"
exit /b 0
