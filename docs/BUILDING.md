<h1 align="center">Building from source</h1>

<p align="center">
  <a href="BUILDING.md">English</a> |
  <a href="BUILDING.ru.md">Русский</a>
</p>

## Requirements

| Tool | Version |
| --- | --- |
| Visual Studio 2022 | workload *Universal Windows Platform development* |
| Windows SDK | 10.0.22621 |
| .NET SDK | 8.0 |
| .NET Framework | 4.8 (installer and uninstaller) |

The widget is a UWP app compiled with .NET Native, so it must be built with MSBuild from Visual Studio, not with `dotnet build`.

## Everything at once

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```

This publishes the panel as a self-contained single file, builds the widget and the uninstaller, packs them into `installer\VlcControl.Setup\payload.zip` and produces `artifacts\dist\VLCControl-Setup.exe`.

## Individual pieces

```powershell
# panel only (self-contained, single file)
dotnet publish VlcControl\VlcControl.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o VlcControl\publish

# widget + launcher (MSBuild, .NET Native)
powershell -ExecutionPolicy Bypass -File tools\build-widget.ps1

# register the widget package and grant the loopback exemption
powershell -ExecutionPolicy Bypass -File tools\install-widget.ps1

# configure the VLC HTTP interface (vlcrc)
powershell -ExecutionPolicy Bypass -File tools\setup-vlc.ps1
```

> The widget build **always** starts by deleting `obj` and `bin`. Incremental .NET Native builds occasionally leave broken artifacts: the package registers but silently fails to start (`TWinUI 5961`, “the app did not start”).

## Project layout

```
VlcControl/                  panel (WPF, .NET 8, self-contained single file)
  MainWindow.xaml(.cs)       dark panel UI
  Services/VlcController.cs  player logic, playlist, auto-launch of VLC
  VlcClient.cs               VLC HTTP client
  Services/Lang.cs           language detection and switching
  Resources/Strings.*.xaml   RU/EN string tables
installer/
  Shared/Lang.cs             language helpers shared by setup and uninstall
  VlcControl.Setup/          installer (WPF, .NET Framework 4.8, payload inside)
  VlcControl.Uninstall/      uninstaller
src/
  VlcGameBarWidget/          Game Bar widget (UWP)
    UiStrings.cs             RU/EN string table
  VlcLauncher/               launcher that starts VLC with HTTP
tools/                       build and install scripts
```

## Manual widget install

If you would rather not use the installer:

1. Enable developer mode: **Settings → System → For developers**.
2. Configure the VLC HTTP interface: **Tools → Preferences → All → Interface → Main interfaces → Web** (host `127.0.0.1`, port `8080`, password `gamebar`).
3. Register the package: `Add-AppxPackage -Register src\VlcGameBarWidget\bin\x64\Release\ilc\AppxManifest.xml`.
4. Grant the loopback exemption: `CheckNetIsolation LoopbackExempt -a -n=vlcgamebarwidget_11mkq6sn63zaj`.
5. `Win+G` → widgets icon → **VLC Control** → pin it.

## Language

The installer, the uninstaller and the panel use WPF resource dictionaries (`Resources\Strings.ru.xaml`, `Strings.en.xaml`) with `DynamicResource` lookups. The widget has no resource-dictionary support, so it uses a plain string table in `UiStrings.cs`.

The chosen language is stored in `lang.txt`:

- `%LOCALAPPDATA%\Programs\VLCControl\lang.txt` — read by the panel and the installer;
- `%LOCALAPPDATA%\Programs\VLCControl\widget\lang.txt` — read by the widget through its install location;
- `%LOCALAPPDATA%\Packages\vlcgamebarwidget_11mkq6sn63zaj\LocalState\lang.txt` — the widget's own fallback.

The widget also keeps its own override in `ApplicationData.Current.LocalSettings["UiLanguage"]`, which wins over the files.
