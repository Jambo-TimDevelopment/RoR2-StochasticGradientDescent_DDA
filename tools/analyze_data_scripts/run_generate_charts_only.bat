@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM Pipeline:
REM 3) Generate hypothesis charts only (from the latest session_metrics_h1_h4.csv)

set "ROOT_DIR=%~dp0..\.."
pushd "%ROOT_DIR%" || (
  echo [ERROR] Failed to switch to repository root.
  exit /b 1
)

set "CHARTS_PY=tools/analyze_data_scripts/hypotheses_chart_reports/generate_hypotheses_charts.py"
set "CSV_PATH="

for /f "delims=" %%I in ('dir /b /s /a:-d /o:-d "tools\export_data_scripts\posthog_exports\session_metrics_h1_h4.csv" 2^>nul') do (
  if not defined CSV_PATH set "CSV_PATH=%%I"
)

if "%CSV_PATH%"=="" (
  echo [ERROR] Could not find session_metrics_h1_h4.csv under tools/export_data_scripts/posthog_exports
  popd
  exit /b 1
)

echo [STEP 3/3] Generating charts...
echo [INFO] csv path: %CSV_PATH%
python "%CHARTS_PY%" --session-csv "%CSV_PATH%" --reports-root "tools/analyze_data_scripts/hypotheses_chart_reports"
if errorlevel 1 (
  echo [ERROR] Chart generation step failed.
  popd
  exit /b 1
)

echo.
echo [OK] Chart generation finished successfully.
echo [OK] Charts root: tools/analyze_data_scripts/hypotheses_chart_reports
echo.

popd
exit /b 0

