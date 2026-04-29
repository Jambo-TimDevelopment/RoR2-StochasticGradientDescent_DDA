@echo off
setlocal

REM Deletes ALL persons in PostHog project (and their events) via REST API.
REM Prefer keeping secrets in TelemetrySecrets.props (repo root) as:
REM   <TelemetryPostHogPersonalApiKey>...</TelemetryPostHogPersonalApiKey>
REM   <TelemetryPostHogProjectId>123</TelemetryPostHogProjectId>
REM Or set env vars:
REM   POSTHOG_PERSONAL_API_KEY
REM
REM NOTE: events deletion in PostHog Cloud ClickHouse is asynchronous.

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_delete_all_persons.ps1"

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
exit /b %ERRORLEVEL%

