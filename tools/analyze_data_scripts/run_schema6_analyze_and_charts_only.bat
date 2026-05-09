@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Pipeline (without export):
REM 2) Run hypotheses analysis (H1-H4) -> summary_h1_h4.md + session_metrics_h1_h4.csv
REM 3) Generate hypothesis charts from the produced CSV

set "ROOT_DIR=%~dp0..\.."
pushd "%ROOT_DIR%" || (
  echo [ERROR] Failed to switch to repository root.
  exit /b 1
)

set "EXPORT_DIR=tools/export_data_scripts/posthog_exports"
set "ANALYZE_PY=tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py"
set "CHARTS_PY=tools/analyze_data_scripts/hypotheses_chart_reports/generate_hypotheses_charts.py"

set "EVENTS_FILE="
for /f "delims=" %%I in ('dir /b /a:-d /o:-d "tools\export_data_scripts\posthog_exports\ALL_events_schema_ge6_*.jsonl" 2^>nul') do (
  if not defined EVENTS_FILE set "EVENTS_FILE=tools\export_data_scripts\posthog_exports\%%I"
)

if "%EVENTS_FILE%"=="" (
  echo [ERROR] Could not find input events file: %EXPORT_DIR%\ALL_events_schema_ge6_*.jsonl
  popd
  exit /b 1
)

for %%F in ("%EVENTS_FILE%") do set "EVENTS_BASENAME=%%~nF"
set "RUN_TAG=%EVENTS_BASENAME:ALL_events_schema_ge6_=%"
set "OUT_DIR=%EXPORT_DIR%\hypotheses_results_schema_ge6_%RUN_TAG%"
set "CSV_PATH=%OUT_DIR%\session_metrics_h1_h4.csv"

echo [STEP 2/3] Running hypotheses analysis...
echo [INFO] events file: %EVENTS_FILE%
echo [INFO] output dir : %OUT_DIR%
python "%ANALYZE_PY%" "%EVENTS_FILE%" --min-schema-version 6 --out-dir "%OUT_DIR%" --summary-lang ru
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

