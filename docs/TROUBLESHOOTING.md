<h1 align="center">Troubleshooting</h1>

<p align="center">
  <a href="TROUBLESHOOTING.md">English</a> |
  <a href="TROUBLESHOOTING.ru.md">Русский</a>
</p>

## The widget says “No connection”

VLC is either not running, or running without the HTTP interface.

- Press **Launch VLC** in the widget — it starts VLC with the right options, or restarts it with them if VLC is already open without HTTP.
- If that does not help, check that the widget is talking to the right place: host `127.0.0.1`, port `8080`, password `gamebar` (widget settings → the gear icon).

## The widget does not appear in the Game Bar menu

Restart the Game Bar:

```powershell
Stop-Process -Name GameBar,GameBarFTServer -Force -ErrorAction SilentlyContinue
Start-Process ms-gamebar:
```

Then `Win+G` → widgets icon → **VLC Control**.

## The widget is not drawn over an exclusive fullscreen game

That is a Game Bar limitation, not a bug: widgets are only rendered in the overlay, never over an exclusive fullscreen surface. Use borderless windowed mode or fullscreen “optimized” mode.

## The panel does not see VLC

Open the gear icon and check the connection settings: host `127.0.0.1`, port `8080`, password `gamebar`. These are the defaults the installer writes into `%APPDATA%\vlc\vlcrc`.

## Speed or volume does not change

Some codecs refuse `rate` / `volume` commands while playing. Try pausing, or switch to a different file.

## The installer could not register the widget

Developer mode is required for sideloaded apps:

**Settings → System → For developers**. The installer enables it itself when it has administrator rights; if it could not, enable it manually and run the installer again.

## VLC ignores the settings the installer wrote

- If VLC was already running during installation, its settings file was left untouched on purpose. Close VLC and restart it — or run the installer again with VLC closed.
- The installer always keeps a timestamped backup next to the original: `%APPDATA%\vlc\vlcrc.backup-YYYYMMDD-HHMMSS`.

## Where are the logs

| Log | Path |
| --- | --- |
| Installer | `%APPDATA%\VlcControl\setup.log` |
| Uninstaller | `%APPDATA%\VlcControl\uninstall.log` |
| Installer crash | `%APPDATA%\VlcControl\setup-error.log` |
| Widget crash | `%LOCALAPPDATA%\Packages\VlcGameBarWidget_11mkq6sn63zaj\LocalState\crash.log` |

## Start over

Uninstall **VLC Control** from *Settings → Apps*, then delete `%LOCALAPPDATA%\Programs\VLCControl` and `%APPDATA%\VlcControl` if anything is left behind, and run the installer again.
