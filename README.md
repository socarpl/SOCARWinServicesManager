# SOCAR WinServicesManager
<!-- workflow-trigger -->
Work in progress. Developed using OpenAI Codex. 
SOCAR WinServicesManager is a Windows-only toolset for capturing, editing, applying, and automating Windows service startup/state profiles.

The solution contains four executables:

- `Socar.WinServicesManager.exe`: WPF desktop application for profile management, service inspection, dependency visualization, XML import/export, snapshot creation, and Windows Service installation.
- `Socar.WinServicesManager.Cli.exe`: console runner for exported XML profiles.
- `Socar.WinServicesManager.Tray.exe`: normal-user tray app that lists profiles and asks the service process to run them.
- `Socar.WinServicesManager.Service.exe`: Windows Service that runs selected profiles with service-control privileges.

## Branding And Executable Icons

The package is branded as `SOCAR WinServicesManager` and is owned by `SOCAR Software`.

Executable icon assets are stored in:

```text
Assets\
```

Current icon assignments:

- `Assets\gear_14964.ico`: main WPF app executable icon and tray app executable/tray notification icon
- `Assets\cli_gear_108987.ico`: CLI executable icon only
- `Assets\service_gear_265392.ico`: Windows Service executable icon only

The tray app loads its notification-area icon from the tray executable's associated icon, so the tray icon matches `Socar.WinServicesManager.Tray.exe`.

## Core Concepts

### Profiles

A profile is a named set of service actions stored in SQLite. Each service action can independently define:

- target startup type: `Automatic`, `Manual`, `Disabled`, or unchanged
- target state: `Running`, `Stopped`, or unchanged

Services not included in a profile are left unchanged.

When a profile runs, startup-type changes are applied first, then stop actions, then start actions. The runner checks current service values before acting:

- matching startup type is skipped
- already-running services are not started again
- already-stopped services are not stopped again

If a service is requested as `Running` but is disabled or has no known startup type, the runner changes startup type to `Manual` before starting it.

### Snapshots

The WPF app can create a snapshot profile. A snapshot contains every service visible to the service API at capture time, with target values equal to current system values:

- current startup type becomes target startup type
- current `Running` state becomes target `Running`
- every other state becomes target `Stopped`

Snapshots are intended as revert points.

### Dependency Stop Policy

The profile runner supports three policies for stopping a service that has running dependent services:

- `AutoStopDependents`: recursively stop running dependent services first
- `WarnAndSkipUnlessInProfile`: skip the target unless all running dependents are also marked to stop in the profile
- `FailAndContinue`: fail that service and continue with the rest of the profile

The WPF app stores this setting in SQLite. The CLI currently uses `AutoStopDependents`.

## Configuration

All executables use the same config contract.

Config file name:

```text
service-manager.config.json
```

Expected location:

```text
next to the executable that is running
```

Schema:

```json
{
  "databasePath": "C:\\ProgramData\\Socar.WinServicesManager\\service-profiles.db",
  "mainAppPath": "C:\\Program Files\\Socar.WinServicesManager\\Socar.WinServicesManager.exe"
}
```

Both values may be blank:

```json
{
  "databasePath": "",
  "mainAppPath": ""
}
```

Blank values mean "use local defaults".

### Path Resolution

`databasePath` controls where `service-profiles.db` is read/written.

`mainAppPath` controls what executable the tray app launches from its `Open main app` menu item.

Relative paths are resolved relative to the running executable directory. Environment variables inside paths are expanded.

Examples:

```json
{
  "databasePath": ".\\service-profiles.db",
  "mainAppPath": ".\\Socar.WinServicesManager.exe"
}
```

```json
{
  "databasePath": "%ProgramData%\\Socar.WinServicesManager\\service-profiles.db",
  "mainAppPath": "%ProgramFiles%\\Socar.WinServicesManager\\Socar.WinServicesManager.exe"
}
```

### Environment Overrides

Environment variables override JSON values:

```text
WINDOWS_SERVICE_MANAGER_DB
WINDOWS_SERVICE_MANAGER_APP
```

Use these only when you need process/account-specific overrides, such as a Windows Service running under a different account.

### First-Run Repair

The main WPF app repairs config on startup:

- if `service-manager.config.json` does not exist, it creates it
- if `databasePath` is empty, it writes `<main app folder>\service-profiles.db`
- if `mainAppPath` is empty, it writes the full path to the currently running main executable

This means a fresh packaged folder becomes self-configuring after the first elevated launch of `Socar.WinServicesManager.exe`.

The shared config loader also creates a blank/default config file if missing. The main app then fills blank values with concrete paths.

### Config Deployment Guidance

If all executables are in the same folder, one config file in that folder is enough:

```text
publish\
  Socar.WinServicesManager.exe
  Socar.WinServicesManager.Cli.exe
  Socar.WinServicesManager.Tray.exe
  Socar.WinServicesManager.Service.exe
  service-manager.config.json
  service-profiles.db
```

If executables are installed in different folders, copy `service-manager.config.json` next to each executable and point every `databasePath` to the same database file.

Recommended shared database location for installed systems:

```text
C:\ProgramData\Socar.WinServicesManager\service-profiles.db
```

## SQLite Database

Database file:

```text
service-profiles.db
```

Default location:

```text
same directory as the running executable
```

Effective location:

1. `WINDOWS_SERVICE_MANAGER_DB`, if set
2. `databasePath` from `service-manager.config.json`, if non-empty
3. `<executable folder>\service-profiles.db`

The database stores:

- profiles
- profile service actions
- app settings, including dependency stop policy

The Windows Service reads fresh data from SQLite for every tray-triggered profile run. It does not cache profile contents.

## Main WPF App

The main app requires administrator rights and relaunches itself through UAC if needed.

Main features:

- CRUD profiles
- run profiles
- create snapshot profiles
- import/export profiles as XML
- configure dependency stop policy
- inspect services
- view selected-service dependency tree
- view full service dependency graph
- install/uninstall the background Windows Service

The top application menu uses standard WPF menus:

- `Profile`
- `Tools`
- `Windows Service`
- `Help`

Menu dropdown placement is forced to left-aligned behavior at startup. This avoids Windows/WPF right-handed menu alignment causing dropdowns to open leftward from the top menu item.

### Profiles Window

The Profiles window is the first screen of the main app. It lists stored profiles in a grid with:

- profile name
- number of service actions
- last updated timestamp

Supported actions:

- create a profile
- edit the selected profile
- delete the selected profile
- run the selected profile
- snapshot the current system into a new profile
- export the selected profile to XML
- import a profile XML file
- open settings
- open service manager
- open service graph
- install or uninstall the background Windows Service

The profiles grid supports a row context menu. Right-clicking a profile row selects that row and shows:

- `Run`
- separator
- `Edit`
- `Delete`

Right-clicking empty grid space does not show profile actions.

### Create/Edit Profile Window

The Create/Edit Profile window shows the full service list used to build a profile.

Top controls:

- profile name
- service search box
- `Hide all Microsoft services`, selected by default

The services grid contains:

- `Use`
- display name
- service name
- current startup type
- current status
- new startup type
- new status

Grid behavior:

- column sorting is enabled through WPF `DataGrid` column headers
- changing `New startup` or `New status` automatically checks `Use`
- only rows with `Use` checked are saved into the profile
- unchanged target values are ignored when saving actions

Bulk controls sit between the grid and the details tabs:

- `Bulk Set: New Startup`
- `Bulk Set: New Status`

Bulk operation behavior:

- select one or more rows by checking `Use`
- choose a target value from the bulk dropdown
- click `Set`
- only currently visible rows with `Use` checked are changed
- hidden rows filtered out by search or `Hide all Microsoft services` are not changed by the bulk action

The bottom tabs show details for the selected service:

- `Details`: service metadata formatted as text
- `Dependency tree`: service dependencies and dependent services

The services grid supports a row context menu. Right-clicking a service row selects that row and shows:

- `Open file location`
- `Search <exe name> in Google`

`Open file location` parses the service binary path, expands environment variables, and opens Explorer at the executable location. If the file exists, Explorer selects the executable. If only the directory exists, Explorer opens the directory.

`Search <exe name> in Google` opens the default browser with a Google search using only the executable filename, not the full path.

Service binary path parsing handles common Windows service formats:

- quoted executable paths with arguments
- unquoted executable paths ending in `.exe`
- paths containing environment variables such as `%SystemRoot%`

### Service Graph Window

The graph window visualizes all services and their dependency relationships as a force-style graph.

Graph behavior:

- each service is a draggable dot
- service names are displayed under dots
- dependency lines include arrowheads pointing toward the required parent service
- dots are spaced to reduce overlap
- clicking a dot updates the service information panel
- running services are green with a black border
- stopped services are red with a white border

The graph window has a service information panel on the left showing:

- service name
- short service name
- description

The Profiles window has a `Graph` menu entry that opens the graph as a separate window alongside the main Profiles window.

### Windows Service Installer Dialog

Button:

```text
install-windows-service
```

The dialog shows:

- whether the service is installed
- whether the service is running

Actions:

- `Install service`
- `Uninstall service`

Install assumptions:

- `Socar.WinServicesManager.Service.exe` is in the same directory as `Socar.WinServicesManager.exe`
- service name is `SOCARWinServicesManagerProfileRunner`
- service display name is `SOCAR WinServicesManager Profile Runner`
- startup is automatic
- account is `LocalSystem`

`LocalSystem` is intentional: the service must have privileges to start and stop other Windows services while the tray app remains a normal user process.

## Tray App And Windows Service

The tray app is a normal user process. It does not manipulate services directly.

The Windows Service performs privileged work.

Tray app behavior:

- lives in the Windows notification area
- left-click opens a context menu
- menu lists profiles read from the shared SQLite database
- clicking a profile sends a run request to the Windows Service
- `Open main app` launches `Socar.WinServicesManager.exe` using `mainAppPath` from config
- `Refresh menu` reloads the profile list from SQLite
- `About` shows branding and ownership text
- `Exit` closes the tray app

The tray app intentionally runs as a normal user process. This avoids a UAC prompt at every user logon. Privileged work is delegated to the Windows Service.

Communication:

- IPC mechanism: Windows Named Pipes
- pipe name: `Socar.WinServicesManager.ProfileRunner`
- tray client: `NamedPipeClientStream`
- service server: `NamedPipeServerStream`

Message format:

```text
RUN_PROFILE_ID:<profileId>
```

Service response:

```text
OK
<profile run log>
```

or:

```text
ERROR: <message>
```

The service pipe ACL allows authenticated non-admin users to connect. This allows the tray app to send requests without UAC.

## CLI Runner

Run an exported XML profile:

```powershell
dotnet run --project .\WindowsServiceManager.Cli\WindowsServiceManager.Cli.csproj -- .\profile.xml
```

Skip confirmation:

```powershell
dotnet run --project .\WindowsServiceManager.Cli\WindowsServiceManager.Cli.csproj -- .\profile.xml -f
```

Show help:

```powershell
dotnet run --project .\WindowsServiceManager.Cli\WindowsServiceManager.Cli.csproj -- -h
```

Safety behavior:

- help is available without admin
- without admin, the CLI refuses to load/list/apply XML
- `-f` does not bypass admin checks
- the CLI performs a second admin check immediately before applying changes

## XML Profile Format

Exported XML contains the profile name and service actions.

Example:

```xml
<serviceProfile name="Example">
  <actions>
    <service name="Spooler" displayName="Print Spooler" startupType="Manual" status="Running" />
    <service name="SomeService" displayName="Some Service" startupType="Disabled" status="Stopped" />
  </actions>
</serviceProfile>
```

Import behavior:

- invalid or empty XML is rejected
- if the imported name already exists, the app appends `(2)`, `(3)`, etc.

## Build

Build the main WPF app:

```powershell
dotnet build
```

Build individual projects:

```powershell
dotnet build .\WindowsServiceManager.Cli\WindowsServiceManager.Cli.csproj
dotnet build .\WindowsServiceManager.Tray\WindowsServiceManager.Tray.csproj
dotnet build .\WindowsServiceManager.Service\WindowsServiceManager.Service.csproj
```

## Package

Publish all executables into one folder:

```powershell
.\publish.ps1
```

Output:

```text
.\publish
```

The script publishes:

- WPF app
- CLI app
- tray app
- Windows Service
- shared blank `service-manager.config.json`

Use a self-contained package if the target machine may not have the .NET runtime:

```powershell
.\publish.ps1 -SelfContained
```

## GitHub Release Automation

The repository includes a GitHub Actions workflow:

```text
.github/workflows/release.yml
```

On every push to `master`, GitHub Actions:

- checks out the repository
- installs .NET 8
- runs `publish.ps1`
- zips the `publish` folder
- uploads the ZIP as a workflow artifact
- finds the highest existing `v1.0.N` release tag
- creates the next patch release tag, for example `v1.0.1`, `v1.0.2`, `v1.0.3`
- publishes a normal GitHub Release with the ZIP attached
- removes the old legacy `continuous` prerelease/tag if it exists

Download asset name pattern:

```text
SOCAR-WinServicesManager-v1.0.N-win-x64.zip
```

## Recovery And Repair

### Missing Config

If `service-manager.config.json` is missing:

- shared runtime creates it
- main WPF app fills blank paths on startup

### Blank Config

If paths are blank:

- main WPF app writes concrete local defaults
- tray/service/CLI fall back to local defaults unless environment overrides are set

### Wrong Database Path

Fix `databasePath` in every deployed `service-manager.config.json`, or set:

```text
WINDOWS_SERVICE_MANAGER_DB
```

Make sure the service account can read/write the target folder.

### Tray Cannot Open Main App

Fix `mainAppPath` in the tray app config, or set:

```text
WINDOWS_SERVICE_MANAGER_APP
```

### Tray Cannot Run Profiles

Check:

- `SOCARWinServicesManagerProfileRunner` service is installed
- service is running
- tray and service use the same `databasePath`
- named pipe is available

### Service Installed But Cannot See Profiles

Most likely the service has a different config file or database path. Put the same `service-manager.config.json` next to `Socar.WinServicesManager.Service.exe`, or use an absolute `databasePath` such as:

```text
C:\ProgramData\Socar.WinServicesManager\service-profiles.db
```

### Service Cannot Change Services

Verify the service runs as `LocalSystem`. The built-in installer dialog creates it with:

```text
obj= LocalSystem
```

## Requirements

- Windows
- .NET 8 runtime for framework-dependent publish
- administrator rights for the main app, CLI profile application, and service installation
