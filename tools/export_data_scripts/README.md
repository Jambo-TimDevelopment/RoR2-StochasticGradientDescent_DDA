# Export Data Scripts

Scripts in this folder are used to export/delete telemetry data and run utility export actions.

## Quick start

Run from repository root:

```powershell
# Export all events + persons (schema filter can be set in .bat/.ps1)
python --version
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/posthog_export_all.ps1"
```

```powershell
# Export only last hour events
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/posthog_export_last_hour.ps1"
```

```powershell
# Export selected event stream (defaults are set inside script)
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/posthog_export_events.ps1"
```

```powershell
# Send one test event
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/posthog_send_test_event.ps1"
```

```powershell
# Delete all persons (destructive)
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/posthog_delete_all_persons.ps1"
```

```powershell
# Install built DLL to RoR2 BepInEx/plugins
powershell -ExecutionPolicy Bypass -File "tools/export_data_scripts/InstallToRor2.ps1" -DllPath "<path-to-GeneticsArtifact.dll>"
```

## Notes

- Prefer using values from `TelemetrySecrets.props` or environment variables for PostHog credentials.
- `.bat` wrappers in this folder call matching `.ps1` scripts.
