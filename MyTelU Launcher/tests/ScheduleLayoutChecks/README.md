# Schedule Layout Checks

Build the main launcher first, then run:

```powershell
dotnet run --project tests/ScheduleLayoutChecks/ScheduleLayoutChecks.csproj
```

The default assembly path is the launcher's Debug x64 build directory. For a
different build output, pass `-p:ScheduleBuildDir="S:\path\to\build\"`.
The compiled launcher initializes Windows App SDK when loaded. When testing
against a self-contained publish, add its runtime folder to the process PATH
so its native DLLs can be resolved, for example:

```powershell
$env:PATH = "S:\path\to\runtime;$env:PATH"
dotnet run --project tests/ScheduleLayoutChecks/ScheduleLayoutChecks.csproj -p:ScheduleBuildDir="S:\path\to\build\"
```

These checks exercise all 16 transitions between List, Table, Week, and
Timeline against the compiled view model, including exclusive selection,
notifications, and toolbar icons. They also check the actual XAML's radio
options, lazy loading, grouped data binding, and scroll support.

Layout checks skip the view-model constructor. Academic refresh checks use
in-memory services to exercise expired-session recovery, dropdown selection
events, refresh ordering, and offline fallback. No test signs in, makes network
requests, sends reminders, or changes real settings/cache files. These checks
do not replace visual testing of the application or its Native AOT build.
