# Analyze Data Scripts

Scripts in this folder process exported JSONL telemetry and produce analysis artifacts.

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
- `tools/export_data_scripts/posthog_exports/hypotheses_results/sensor_calibration_hints.md`
