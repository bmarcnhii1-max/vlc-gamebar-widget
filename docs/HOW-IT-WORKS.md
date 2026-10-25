<h1 align="center">How it works</h1>

<p align="center">
  <a href="HOW-IT-WORKS.md">English</a> |
  <a href="HOW-IT-WORKS.ru.md">Русский</a>
</p>

## The big picture

```
Xbox Game Bar ──► VlcGameBarWidget (UWP)  ─┐
                                            ├─► VLC HTTP interface ──► VLC
Standalone panel ──► VlcControl.exe (WPF) ──┘   127.0.0.1:8080
```

Both front ends speak the same protocol to the same place, so they behave identically — the only difference is where the window is drawn.

## The VLC side

VLC exposes a documented HTTP interface once the `http` interface is enabled. The installer enables it by editing `%APPDATA%\vlc\vlcrc` (with a timestamped backup of the original):

```
extraintf=http
http-host=127.0.0.1
http-port=8080
http-password=gamebar
```

Everything is plain HTTP with Basic auth (`:` + password), served from `http://127.0.0.1:8080/requests/`.

| Request | Purpose |
| --- | --- |
| `GET status.xml` | current state, time, length, rate, volume, track metadata |
| `GET playlist.xml` | playlist entries with id, name and duration |
| `GET status.xml?command=pl_pause` | play / pause |
| `GET status.xml?command=pl_next` | next track |
| `GET status.xml?command=pl_previous` | previous track |
| `GET status.xml?command=pl_play&id=N` | play playlist item `N` |
| `GET status.xml?command=seek&val=+10S` | seek 10 seconds forward (also `-10S`) |
| `GET status.xml?command=seek&val=42%25` | seek to 42 % of the track |
| `GET status.xml?command=rate&val=1.5` | playback speed |
| `GET status.xml?command=volume&val=200` | volume, 0–512, where 256 = 100 % |

The panel and the widget poll `status.xml` roughly once a second and re-render the title, time, volume and speed from it. Because the interface lives in VLC itself, both work whether VLC is in the foreground, in the background, or behind a fullscreen game.

## Launching VLC

The **Launch VLC** button does not need VLC to be configured in advance. `VlcLauncher.exe` (shipped inside the widget package as a `fullTrustProcess`) checks whether the HTTP interface already answers on `127.0.0.1:8080`:

- not running → it starts `vlc.exe` with `--extraintf=http --http-host=127.0.0.1 --http-port=8080 --http-password=gamebar`;
- running without HTTP → it stops that instance and starts a new one with the same arguments;
- already answering → it does nothing.

`vlc.exe` is looked up in `%ProgramFiles%\VideoLAN\VLC`, `%ProgramFiles(x86)%\VideoLAN\VLC`, `%LOCALAPPDATA%\Programs\VideoLAN\VLC` and next to the launcher.

## The Game Bar widget

The widget is a UWP app that registers itself as a Game Bar app extension (`windows.appExtension` with the `microsoft.gameBarUIExtension` category).

1. Game Bar activates the app with the `ms-gamebarwidget:` protocol.
2. `App.OnActivated` recognizes `ActivationKind.Protocol` and casts the arguments to `XboxGameBarWidgetActivatedEventArgs`.
3. It creates `new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame)` and navigates the frame to `Widget1`, which becomes the widget's XAML content inside the Game Bar overlay.

The private Game Bar interfaces are declared in `Package.appxmanifest` under a `windows.activatableClass.proxyStub` extension, so the app can be activated by the Game Bar host.

## The installer

`VLCControl-Setup.exe` is a .NET Framework 4.8 WPF app with `payload.zip` embedded as a resource. Its steps:

1. **Prepare** — stop running `GameBar`, `GameBarFTServer`, `VlcGameBarWidget` and `VlcControl` processes.
2. **Copy** — unpack `payload.zip` into `%LOCALAPPDATA%\Programs\VLCControl` (`app\`, `widget\`, `Uninstall.exe`).
3. **Widget** — `Add-AppxPackage -Register` on `widget\AppxManifest.xml`, enable developer mode if it was off (remembering the old state in `install-state.ini`), and add the loopback exemption with `CheckNetIsolation LoopbackExempt -a`.
4. **VLC** — back up and edit `vlcrc` to enable the HTTP interface.
5. **Shortcuts** — create the desktop and Start menu shortcuts, and write the *Apps & features* uninstall entry.

The uninstaller reverses all of it and restores developer mode to the state recorded in `install-state.ini`.

## Why the loopback exemption

A packaged UWP app is an AppContainer and is not allowed to talk to `127.0.0.1` by default. `CheckNetIsolation LoopbackExempt -a -n=vlcgamebarwidget_11mkq6sn63zaj` grants exactly that package the permission, which is what makes the widget able to reach VLC's HTTP interface.

## Language handling

| Part | Mechanism |
| --- | --- |
| Panel, installer, uninstaller | WPF `ResourceDictionary` (`Strings.ru.xaml` / `Strings.en.xaml`) + `{DynamicResource}` |
| Game Bar widget | string table in `UiStrings.cs` (UWP has no dynamic resources) |

The language is stored in a plain `lang.txt` file containing `ru` or `en`. The installer writes it to the install directory, to the widget's install directory and to the widget's `LocalState` folder; the widget additionally keeps its own override in `LocalSettings["UiLanguage"]`, which takes priority.
