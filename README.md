# Disk Monitor

Disk Monitor is a lightweight Windows 11 startup-readiness dashboard. It samples
the aggregate physical disk's active time once per second and reports **READY**
only after activity stays below the configured threshold continuously for the
configured duration.

## Architecture

- **DiskMonitor.App** — Windows Forms dashboard, readiness settings, and app lifecycle.
- **DiskMonitor.Core** — platform-independent active-time models and readiness evaluation.
- **DiskMonitor.Windows** — Windows `PhysicalDisk(_Total)\% Disk Time` collection and current-user startup registration.
- **DiskMonitor.Tests** — xUnit tests for readiness, configuration, startup registration, and real Windows counter collection.

The app reads the Windows `% Disk Time` active-time counter directly, avoiding
negative values caused by subtracting a rounded `% Idle Time` reading from 100.
Invalid or unavailable readings are shown as unavailable, never as zero, and
reset the readiness timer. Disk activity is a measure of busy time, not a
diagnosis of disk failure or a guarantee that every application will perform
well.

## Installation and running

### Build from source

Requires Windows 11 and the .NET 10 SDK:

```powershell
dotnet build .\DiskMonitor.sln -c Release
dotnet test .\DiskMonitor.sln -c Release
dotnet run --project .\DiskMonitor.App\DiskMonitor.App.csproj
```

### Self-contained release

Publish a self-contained Windows x64 single-file executable:

```powershell
dotnet publish .\DiskMonitor.App\DiskMonitor.App.csproj -c Release -p:PublishProfile=win-x64
```

The app executable is written to
`artifacts\publish\win-x64-readiness\DiskMonitor.App.exe`. Copy it to a
user-writable folder and launch it; a separate .NET or Python installation is
not required.

## Startup configuration

On launch, Disk Monitor registers its executable in the current user's Windows
Run key so the dashboard opens at sign-in. Registration does not require
administrator access. If registration fails, the dashboard displays a warning.
To stop it starting automatically, remove the `DiskMonitor` value from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Dashboard and readiness behavior

The dark dashboard prominently shows whether you can start your workflow or
should wait, along with current disk active time, stability progress, and a
recent-activity graph. Disk Monitor does not send toast or balloon
notifications. **Settings** lets you configure:

- The active-time threshold (default **20%**).
- How long activity must stay below that threshold (default **60 seconds**).

The status is **INITIALIZING** until the first valid counter sample is
available. It then shows **PLEASE WAIT** while the disk is at or above the
threshold or the stability period is incomplete. Any reading at or above the
threshold, invalid metric, or sampling gap resets the stability period. Once
the full duration passes below threshold, status becomes **READY TO GO** and
the dashboard says you can start your workflow. Monitoring continues after
that, and a later threshold crossing changes the status back to **PLEASE WAIT**.
Closing the window stops monitoring and exits the application; there is no tray
or background monitoring process.

Settings are stored locally at
`%LocalAppData%\DiskMonitor\settings.json`.

## Troubleshooting

- **Active time is unavailable:** Windows may not expose the
  `PhysicalDisk(_Total)\% Disk Time` counter, or the counter may be temporarily
  unavailable. The dashboard will not count unavailable values as zero or
  advance the stability timer.
- **The status stays PLEASE WAIT:** disk activity must remain strictly below
  the configured threshold for the entire stable duration. A spike, invalid
  reading, or sampling gap restarts the timer.
- **No startup launch:** verify the current user's Run key contains the
  `DiskMonitor` entry and that its executable path still exists. Reopen the
  dashboard to refresh the registration path.
- **Settings cannot be loaded:** the settings file is JSON. If it is malformed,
  close the app and remove `%LocalAppData%\DiskMonitor\settings.json` to restore
  the defaults.

## Uninstall

1. Close Disk Monitor; this stops sampling and exits the app.
2. Remove the `DiskMonitor` value from the current user's Run key.
3. Delete the folder containing `DiskMonitor.App.exe`.
4. Optionally remove `%LocalAppData%\DiskMonitor` to delete local settings.

The app does not install a Windows service, modify other processes, or transmit
monitoring data outside the machine.

## Known limitations

- The dashboard uses the aggregate `_Total` physical-disk counter; it does not
  distinguish individual drives.
- This is a startup-readiness aid, not a guarantee of application performance.
- Windows may report temporary unavailable samples, which conservatively reset
  the stability timer.
