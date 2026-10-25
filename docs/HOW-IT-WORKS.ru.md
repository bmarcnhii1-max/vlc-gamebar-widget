<h1 align="center">Как это работает</h1>

<p align="center">
  <a href="HOW-IT-WORKS.md">English</a> |
  <a href="HOW-IT-WORKS.ru.md">Русский</a>
</p>

## Общая схема

```
Xbox Game Bar ──► VlcGameBarWidget (UWP)  ─┐
                                            ├─► HTTP-интерфейс VLC ──► VLC
Панель ─────────► VlcControl.exe (WPF) ─────┘   127.0.0.1:8080
```

Обе оболочки говорят с VLC по одному и тому же протоколу, поэтому ведут себя одинаково — разница только в том, где нарисовано окно.

## Сторона VLC

VLC отдаёт документированный HTTP-интерфейс, если включён интерфейс `http`. Установщик включает его правкой `%APPDATA%\vlc\vlcrc` (с резервной копией оригинала с меткой времени):

```
extraintf=http
http-host=127.0.0.1
http-port=8080
http-password=gamebar
```

Всё это обычный HTTP с Basic-авторизацией (`:` + пароль) по адресу `http://127.0.0.1:8080/requests/`.

| Запрос | Назначение |
| --- | --- |
| `GET status.xml` | состояние, время, длина, скорость, громкость, метаданные трека |
| `GET playlist.xml` | элементы плейлиста с id, именем и длительностью |
| `GET status.xml?command=pl_pause` | пауза / воспроизведение |
| `GET status.xml?command=pl_next` | следующий трек |
| `GET status.xml?command=pl_previous` | предыдущий трек |
| `GET status.xml?command=pl_play&id=N` | включить элемент плейлиста `N` |
| `GET status.xml?command=seek&val=+10S` | перемотка на 10 секунд вперёд (бывает `-10S`) |
| `GET status.xml?command=seek&val=42%25` | перемотка на 42 % трека |
| `GET status.xml?command=rate&val=1.5` | скорость воспроизведения |
| `GET status.xml?command=volume&val=200` | громкость, 0–512, где 256 = 100 % |

Панель и виджет опрашивают `status.xml` примерно раз в секунду и по нему обновляют название, время, громкость и скорость. Интерфейс живёт внутри самого VLC, поэтому обе оболочки работают и когда VLC свёрнут, и когда он в фоне, и когда игра на весь экран.

## Запуск VLC

Кнопке **«Запустить VLC»** не нужно заранее настроенный VLC. `VlcLauncher.exe` (внутри пакета виджета, как `fullTrustProcess`) проверяет, отвечает ли HTTP-интерфейс на `127.0.0.1:8080`:

- не запущен → стартует `vlc.exe` с `--extraintf=http --http-host=127.0.0.1 --http-port=8080 --http-password=gamebar`;
- запущен без HTTP → останавливает этот экземпляр и запускает новый с теми же аргументами;
- уже отвечает → ничего не делает.

`vlc.exe` ищется в `%ProgramFiles%\VideoLAN\VLC`, `%ProgramFiles(x86)%\VideoLAN\VLC`, `%LOCALAPPDATA%\Programs\VideoLAN\VLC` и рядом с лаунчером.

## Виджет Game Bar

Виджет — это UWP-приложение, которое регистрирует себя как расширение приложений Game Bar (`windows.appExtension` с категорией `microsoft.gameBarUIExtension`).

1. Game Bar активирует приложение по протоколу `ms-gamebarwidget:`.
2. `App.OnActivated` распознаёт `ActivationKind.Protocol` и приводит аргументы к `XboxGameBarWidgetActivatedEventArgs`.
3. Создаётся `new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame)`, и фрейм навигирует на `Widget1` — он и становится содержимым виджета внутри оверлея Game Bar.

Приватные интерфейсы Game Bar объявлены в `Package.appxmanifest` в расширении `windows.activatableClass.proxyStub` — без этого Game Bar не сможет активировать приложение.

## Установщик

`VLCControl-Setup.exe` — WPF-приложение на .NET Framework 4.8, внутри которого как ресурс лежит `payload.zip`. Шаги:

1. **Подготовка** — закрыть запущенные `GameBar`, `GameBarFTServer`, `VlcGameBarWidget` и `VlcControl`.
2. **Копирование файлов** — распаковать `payload.zip` в `%LOCALAPPDATA%\Programs\VLCControl` (`app\`, `widget\`, `Uninstall.exe`).
3. **Виджет** — `Add-AppxPackage -Register` для `widget\AppxManifest.xml`, включить режим разработчика, если он был выключен (старое состояние пишется в `install-state.ini`), и выдать loopback-исключение через `CheckNetIsolation LoopbackExempt -a`.
4. **Настройки VLC** — сделать бэкап `vlcrc` и включить HTTP-интерфейс.
5. **Ярлыки и удаление** — ярлыки на рабочем столе и в меню «Пуск», запись в «Программы и компоненты».

Деинсталлятор откатывает всё это и возвращает режим разработчика в состояние, записанное в `install-state.ini`.

## Зачем нужен loopback-исключение

Упакованное UWP-приложение работает в AppContainer и по умолчанию не может обращаться к `127.0.0.1`. `CheckNetIsolation LoopbackExempt -a -n=vlcgamebarwidget_11mkq6sn63zaj` выдаёт это право ровно одному пакету — благодаря этому виджет и достучивается до HTTP-интерфейса VLC.

## Как устроен язык

| Часть | Механизм |
| --- | --- |
| Панель, установщик, деинсталлятор | WPF-словари ресурсов (`Strings.ru.xaml` / `Strings.en.xaml`) + `{DynamicResource}` |
| Виджет Game Bar | таблица строк в `UiStrings.cs` (в UWP нет динамических ресурсов) |

Язык хранится в обычном файле `lang.txt` со значением `ru` или `en`. Установщик пишет его в папку установки, в папку установки виджета и в `LocalState` виджета; у виджета есть ещё и своё переопределение в `LocalSettings["UiLanguage"]`, которое важнее файлов.
