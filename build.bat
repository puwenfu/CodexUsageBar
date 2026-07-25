@echo off
setlocal
set "PROJECT_DIR=%~dp0"
dotnet build "%PROJECT_DIR%CodexUsageBar.sln" --configuration Release --nologo
exit /b %errorlevel%
