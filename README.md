# Disk Monitor

A lightweight Windows 11 disk monitoring utility.

## Prerequisites

- Windows 11 to run the Windows Forms application.
- .NET 10 SDK to build the solution.

The Windows projects enable Windows targeting so that they can also be built
from non-Windows environments with the required .NET targeting packs. The
Windows Forms application itself can only run on Windows.

## Build and test

From the repository root, run:

```powershell
dotnet build .\DiskMonitor.sln
dotnet test .\DiskMonitor.sln
```

## Run the application

From the repository root on Windows, run:

```powershell
dotnet run --project .\DiskMonitor.App\DiskMonitor.App.csproj
```

This opens the live dashboard and starts monitoring in the background. Closing
the window hides it in the notification area; monitoring continues until
**Exit** is selected from the Disk Monitor tray icon menu.

The notification-area menu provides **Open Dashboard**, **Current Status**,
**Pause Monitoring**, **Resume Monitoring**, **Start with Windows**, and
**Exit**. The startup option is off by default. When enabled, the application
registers its executable in the current user's Windows Run key (no
administrator access) and saves the preference to
`%LocalAppData%\DiskMonitor\settings.json`. On sign-in, the dashboard opens,
monitoring starts, and a balloon notification reports the first readiness
assessment after the baseline is evaluated.

Warning, critical, and recovery transitions are delivered as notification
balloons and are deduplicated for each incident. Notification delivery is
isolated behind `INotificationService` for a future toast implementation.

## Inspect live disk metrics

The diagnostics console samples the Windows `PhysicalDisk(_Total)` counters
once per second and exits after five samples. On Windows, run:

```powershell
dotnet run --project .\DiskMonitor.Diagnostics\DiskMonitor.Diagnostics.csproj
```

Press Ctrl+C to cancel early. Unavailable metrics are shown as unavailable
along with the reason; capacity and free space are reported separately for the
system volume.

## Project structure

```text
DiskMonitor.sln
DiskMonitor.App/       Windows Forms application and entry point
  Forms/               Application windows
DiskMonitor.Core/      Platform-independent models and business logic
  Configuration/
  Interfaces/
  Models/
  Services/
DiskMonitor.Windows/   Windows-specific integrations
  Configuration/
  Logging/
  Performance/
DiskMonitor.Tests/     xUnit tests (references Core)
DiskMonitor.Diagnostics/ Console harness for live Windows metrics
```

`DiskMonitor.Core` targets `net10.0` and has no Windows-specific references.
The App references Core and Windows; Windows references Core; Tests references
Core; Diagnostics references Windows.

Health and readiness thresholds can be customized through
`DiskMonitor.Core.Configuration.MonitoringThresholds`. Defaults include a 90%
active-time warning sustained for 20 seconds, critical response time at 100 ms
for 10 seconds, and recovery below 70% active time with response below 50 ms
for 30 seconds. Critical active time requires elevated response time as well;
active time alone is not treated as disk failure.
