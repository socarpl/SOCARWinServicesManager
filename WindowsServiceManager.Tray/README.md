# SOCAR WinServicesManager Tray App

This tray app reads profiles from the shared SQLite database and sends selected
profile IDs to the SOCAR WinServicesManager Profile Runner service over a named
pipe.

Create `service-manager.config.json` next to the tray executable when the tray app
is not installed next to the main app/service:

```json
{
  "databasePath": "C:\\ProgramData\\Socar.WinServicesManager\\service-profiles.db",
  "mainAppPath": "C:\\Program Files\\Socar.WinServicesManager\\Socar.WinServicesManager.exe"
}
```

Environment variables can still override the JSON file:

- `WINDOWS_SERVICE_MANAGER_DB`: full path to `service-profiles.db`
- `WINDOWS_SERVICE_MANAGER_APP`: full path to `Socar.WinServicesManager.exe`
