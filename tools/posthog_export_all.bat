@echo off
setlocal EnableExtensions

REM Export ALL PostHog data locally (events + persons) to JSONL.
REM Requires a Personal API key (NOT the ingest token phc_...).
REM Do NOT commit secrets. Prefer setting env var POSTHOG_PERSONAL_API_KEY.

REM Preferred: keep key in TelemetrySecrets.props as TelemetryPostHogPersonalApiKey.
REM Or set env var POSTHOG_PERSONAL_API_KEY.
set "POSTHOG_PERSONAL_API_KEY="
set "POSTHOG_API_HOST=https://us.posthog.com"
REM Preferred: put Project ID into TelemetrySecrets.props as TelemetryPostHogProjectId.
set "POSTHOG_PROJECT_ID="
set "LIMIT=200"
set "SLEEP_MS_BETWEEN_PAGES=0"

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_export_all.ps1"

set "ARGS=-ApiHost "%POSTHOG_API_HOST%" -Limit %LIMIT% -SleepMsBetweenPages %SLEEP_MS_BETWEEN_PAGES%"
if not "%POSTHOG_PERSONAL_API_KEY%"=="" set "ARGS=%ARGS% -PersonalApiKey "%POSTHOG_PERSONAL_API_KEY%""
if not "%POSTHOG_PROJECT_ID%"=="" set "ARGS=%ARGS% -ProjectId %POSTHOG_PROJECT_ID%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%

echo.
pause
exit /b %errorlevel%

