# SOCAR WinServicesManager Profile Runner Service

This Windows Service listens on the named pipe `Socar.WinServicesManager.ProfileRunner`.
Tray clients send a profile ID, and the service runs that profile using the shared
SQLite database.

Create `service-manager.config.json` next to the service executable if the
database is not located next to the service executable:

```json
{
  "databasePath": "C:\\ProgramData\\Socar.WinServicesManager\\service-profiles.db",
  "mainAppPath": "C:\\Program Files\\Socar.WinServicesManager\\Socar.WinServicesManager.exe"
}
```

The service uses `databasePath`; `mainAppPath` is included so the same config file
can be copied next to all executables.

Install example from an elevated terminal:

```powershell
sc.exe create SOCARWinServicesManagerProfileRunner binPath= "C:\Path\Socar.WinServicesManager.Service.exe" start= auto obj= LocalSystem DisplayName= "SOCAR WinServicesManager Profile Runner"
sc.exe start SOCARWinServicesManagerProfileRunner
```
