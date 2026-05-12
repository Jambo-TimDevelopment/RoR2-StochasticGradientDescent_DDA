@echo off
setlocal EnableExtensions

REM Export telemetry events matching telemetry_schema_version (see TELEMETRY_SCHEMA_VERSION).
REM Persons are skipped when filtering by schema (-ExcludePersons; faster).
REM For events+persons use TELEMETRY_SCHEMA_VERSION=0 and posthog_export_full.bat or edit this BAT to omit -ExcludePersons.
REM Pagination: REST GET .../events/ returns newest timestamps first; before= / rewind walk back in time — latest schema-7 rows are fetched early.
REM Requires a Personal API key (NOT the ingest token phc_...).
REM Do NOT commit secrets. Prefer setting env var POSTHOG_PERSONAL_API_KEY.

REM Preferred: keep key in TelemetrySecrets.props as TelemetryPostHogPersonalApiKey.
REM Or set env var POSTHOG_PERSONAL_API_KEY.
set "POSTHOG_PERSONAL_API_KEY="
REM API base for Personal API requests (capture host is TelemetryPostHogHost in props, often *.i.posthog.com).
set "POSTHOG_API_HOST=https://us.posthog.com"
REM Preferred: put Project ID into TelemetrySecrets.props as TelemetryPostHogProjectId.
set "POSTHOG_PROJECT_ID="
REM PostHog allows up to 1000 per page — fewer requests, faster full export.
set "LIMIT=1000"
set "SLEEP_MS_BETWEEN_PAGES=0"
REM Events: telemetry_schema_version >= this (0 = all versions). For a full unfiltered export use posthog_export_full.bat.
set "TELEMETRY_SCHEMA_VERSION=7"

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_export_all.ps1"

set "ARGS=-ApiHost "%POSTHOG_API_HOST%" -Limit %LIMIT% -SleepMsBetweenPages %SLEEP_MS_BETWEEN_PAGES%"
if not "%TELEMETRY_SCHEMA_VERSION%"=="" if not "%TELEMETRY_SCHEMA_VERSION%"=="0" set "ARGS=%ARGS% -TelemetrySchemaVersion %TELEMETRY_SCHEMA_VERSION%"
if not "%TELEMETRY_SCHEMA_VERSION%"=="" if not "%TELEMETRY_SCHEMA_VERSION%"=="0" set "ARGS=%ARGS% -ExcludePersons"
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

