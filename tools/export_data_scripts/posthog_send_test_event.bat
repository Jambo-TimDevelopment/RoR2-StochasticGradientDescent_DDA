@echo off
setlocal EnableExtensions

REM Fill these in (do NOT commit secrets):
set "POSTHOG_API_KEY=phc_REPLACE_ME"
set "POSTHOG_HOST=https://us.i.posthog.com"
set "DISTINCT_ID=debug_user_001"
set "EVENT_NAME=manual_test_event"

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%posthog_send_test_event.ps1"

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" ^
  -ApiKey "%POSTHOG_API_KEY%" ^
  -IngestHost "%POSTHOG_HOST%" ^
  -DistinctId "%DISTINCT_ID%" ^
  -Event "%EVENT_NAME%"

echo.
pause
exit /b %errorlevel%

