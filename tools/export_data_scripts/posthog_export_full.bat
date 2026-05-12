@echo off
setlocal EnableExtensions

REM Full PostHog export: ALL event schema versions (no telemetry_schema_version filter).
REM Same as posthog_export_all.ps1 with -TelemetrySchemaVersion 0.
REM Console progress: rows per page, running total, file size on disk (see .ps1 -ProgressIntervalPages).
REM Secrets: TelemetrySecrets.props or POSTHOG_PERSONAL_API_KEY. Personal API key, NOT phc_...
REM HTTP 500 on a fixed before=: script retries, then tries recovery URLs (-On500BeforeSkipMinutes / -On500ReducedLimit; strips offset=).
REM Example override: -On500BeforeSkipMinutes 15 -On500ReducedLimit 50  (0 disables that knob).
REM Resume after failure: use -ResumeFromStateFile path to the .export_state.json next to the partial JSONL.

set "POSTHOG_PERSONAL_API_KEY="
set "POSTHOG_API_HOST=https://us.posthog.com"
set "POSTHOG_PROJECT_ID="
set "LIMIT=1000"
REM Pause 0 = no artificial delay between pages (fastest). Raise only if PostHog returns 429 (e.g. 50–150).
set "SLEEP_MS_BETWEEN_PAGES=0"
set "TELEMETRY_SCHEMA_VERSION=0"

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_export_all.ps1"

set "ARGS=-ApiHost "%POSTHOG_API_HOST%" -Limit %LIMIT% -SleepMsBetweenPages %SLEEP_MS_BETWEEN_PAGES%"
if not "%TELEMETRY_SCHEMA_VERSION%"=="" if not "%TELEMETRY_SCHEMA_VERSION%"=="0" set "ARGS=%ARGS% -TelemetrySchemaVersion %TELEMETRY_SCHEMA_VERSION%"
if not "%POSTHOG_PERSONAL_API_KEY%"=="" set "ARGS=%ARGS% -PersonalApiKey "%POSTHOG_PERSONAL_API_KEY%""
if not "%POSTHOG_PROJECT_ID%"=="" set "ARGS=%ARGS% -ProjectId %POSTHOG_PROJECT_ID%"

where pwsh >nul 2>&1
if %errorlevel%==0 (
  pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%
)

echo.
pause
exit /b %errorlevel%
