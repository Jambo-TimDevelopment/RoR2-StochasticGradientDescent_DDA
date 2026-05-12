# Analyze Data Scripts

Scripts in this folder process exported JSONL telemetry and produce analysis artifacts.

## Batch pipelines (Windows)

- Schema **≥ 6** export + H1–H4 analysis + charts: `tools/analyze_data_scripts/schema6/run_schema6_export_analyze_charts.bat`
- Schema **≥ 6**, analysis + charts only (reuse latest `ALL_events_schema_ge6_*.jsonl`): `tools/analyze_data_scripts/schema6/run_schema6_analyze_and_charts_only.bat`
- Schema **≥ 7** export + analysis + charts: `tools/analyze_data_scripts/schema7/run_schema7_export_analyze_charts.bat`
- Schema **≥ 7**, analysis + charts only: `tools/analyze_data_scripts/schema7/run_schema7_analyze_and_charts_only.bat`
- Charts only from the newest `session_metrics_h1_h4.csv` under exports: `tools/analyze_data_scripts/run_generate_charts_only.bat`

## Quick start

Run from repository root:

```powershell
# Main H1-H4/H3 axis-first analysis
python "tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl"
```

```powershell
# Same analysis with schema filter
python "tools/analyze_data_scripts/analyze_hypotheses_h1_h4.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl" --min-schema-version 6
```

```powershell
# Inspect session coverage and H2/H4 field presence
python "tools/analyze_data_scripts/inspect_dda_sessions.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl"
```

```powershell
# Calibrate sensor thresholds from exports
python "tools/analyze_data_scripts/calibrate_sgd_sensors.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl"
```

```powershell
# H5/H6 post-session Likert (fairness + continuity); writes session_survey_h5_h6.csv and summary_h5_h6.md
python "tools/analyze_data_scripts/analyze_hypotheses_h5_h6.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl"
```

```powershell
# PNG charts from session_survey_h5_h6.csv (matplotlib)
python "tools/analyze_data_scripts/hypotheses_chart_reports/generate_hypotheses_h5_h6_charts.py" --survey-csv "tools/export_data_scripts/posthog_exports/hypotheses_results/session_survey_h5_h6.csv"
```

```powershell
# Validate H5/H6 survey presence in exports
python "tools/analyze_data_scripts/validate_posthog_survey.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl"
python "tools/analyze_data_scripts/validate_posthog_survey.py" "tools/export_data_scripts/posthog_exports/ALL_events_*.jsonl" --show-ok
```

```powershell
# Optional helper for Huntress-focused session checks
python "tools/analyze_data_scripts/inspect_huntress_sessions.py"
```

## Main outputs

- `tools/export_data_scripts/posthog_exports/hypotheses_results/session_metrics_h1_h4.csv`
- `tools/export_data_scripts/posthog_exports/hypotheses_results/summary_h1_h4.md`
- `tools/export_data_scripts/posthog_exports/hypotheses_results/session_survey_h5_h6.csv`
- `tools/export_data_scripts/posthog_exports/hypotheses_results/summary_h5_h6.md`
- `tools/export_data_scripts/posthog_exports/hypotheses_results/sensor_calibration_hints.md`
- `tools/analyze_data_scripts/hypotheses_chart_reports/runs/<timestamp>/` — PNG from `generate_hypotheses_charts.py` or `generate_hypotheses_h5_h6_charts.py`
