@echo off
setlocal EnableExtensions

REM Export PostHog events for the last hour to a dedicated folder.
REM Requires a Personal API key (NOT the ingest token phc_...).

REM Preferred: keep key in TelemetrySecrets.props as TelemetryPostHogPersonalApiKey.
REM Or set env var POSTHOG_PERSONAL_API_KEY.
set "POSTHOG_PERSONAL_API_KEY="
set "POSTHOG_API_HOST=https://us.posthog.com"
REM Preferred: put Project ID into TelemetrySecrets.props as TelemetryPostHogProjectId.
set "POSTHOG_PROJECT_ID="
set "EVENT_NAME="
set "LIMIT=200"
set "MAX_EVENTS=0"

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_export_last_hour.ps1"
set "OUT_DIR=%SCRIPT_DIR%posthog_exports_last_hour"

set "ARGS=-ApiHost "%POSTHOG_API_HOST%" -Event "%EVENT_NAME%" -Limit %LIMIT% -MaxEvents %MAX_EVENTS% -OutDir "%OUT_DIR%""
if not "%POSTHOG_PERSONAL_API_KEY%"=="" set "ARGS=%ARGS% -PersonalApiKey "%POSTHOG_PERSONAL_API_KEY%""
if not "%POSTHOG_PROJECT_ID%"=="" set "ARGS=%ARGS% -ProjectId %POSTHOG_PROJECT_ID%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%

echo.
pause
exit /b %errorlevel%
