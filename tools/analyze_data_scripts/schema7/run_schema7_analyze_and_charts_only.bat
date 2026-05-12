@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Pipeline (without export): analysis cohort telemetry_schema_version >= 7
REM 2) Run hypotheses analysis (H1-H4) -> summary_h1_h4.md + session_metrics_h1_h4.csv
REM 3) Generate hypothesis charts from the produced CSV

set "ROOT_DIR=%~dp0..\..\.."
pushd "%ROOT_DIR%" || (
  echo [ERROR] Failed to switch to repository root.
  exit /b 1
)

set "EXPORT_DIR=tools/export_data_scripts/posthog_exports"
set "ANALYZE_PY=tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py"
set "CHARTS_PY=tools/analyze_data_scripts/hypotheses_chart_reports/generate_hypotheses_charts.py"

REM Latest run = max(YYYYMMDD_HHMMSS) across hypotheses_results_schema_ge7_* dirs and ALL_events_schema_ge7_*.jsonl (recursive).
set "RESOLVER=tools\analyze_data_scripts\schema7\resolve_schema_ge7_paths.py"
set "EVENTS_FILE="
set "OUT_DIR="
for /f "usebackq tokens=1,* delims==" %%A in (`python "%RESOLVER%" --exports-root "%EXPORT_DIR%"`) do (
  if /i "%%A"=="EVENTS_FILE" set "EVENTS_FILE=%%B"
  if /i "%%A"=="OUT_DIR" set "OUT_DIR=%%B"
)

if not defined EVENTS_FILE (
  echo [ERROR] Could not resolve latest schema-ge7 export. Run: python "%RESOLVER%" --exports-root "%EXPORT_DIR%"
  popd
  exit /b 1
)
if not defined OUT_DIR (
  echo [ERROR] resolve_schema_ge7_paths.py did not return OUT_DIR.
  popd
  exit /b 1
)

set "CSV_PATH=%OUT_DIR%\session_metrics_h1_h4.csv"

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
echo [INFO] Copying raw PostHog JSONL (server export) into report folder...
for %%F in ("%EVENTS_FILE%") do (
  copy /Y "%%~fF" "%OUT_DIR%\%%~nxF" >nul
  if errorlevel 1 (
    echo [ERROR] Failed to copy events file into %OUT_DIR%
    popd
    exit /b 1
  )
)

echo [STEP 2/3] Running hypotheses analysis...
echo [INFO] events file: %EVENTS_FILE%
echo [INFO] output dir : %OUT_DIR%
python "%ANALYZE_PY%" "%EVENTS_FILE%" --min-schema-version 7 --out-dir "%OUT_DIR%" --summary-lang ru
if errorlevel 1 (
  echo [ERROR] Analysis step failed.
  popd
  exit /b 1
)

if not exist "%CSV_PATH%" (
  echo [ERROR] Expected CSV not found: %CSV_PATH%
  popd
  exit /b 1
)

echo [STEP 3/3] Generating charts...
python "%CHARTS_PY%" --session-csv "%CSV_PATH%" --reports-root "tools/analyze_data_scripts/hypotheses_chart_reports"
if errorlevel 1 (
  echo [ERROR] Chart generation step failed.
  popd
  exit /b 1
)

echo.
echo [OK] Pipeline finished successfully.
echo [OK] Analysis output: %OUT_DIR%
echo [OK] Charts root    : tools/analyze_data_scripts/hypotheses_chart_reports
echo.

popd
exit /b 0
