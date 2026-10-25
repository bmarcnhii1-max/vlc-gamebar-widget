<h1 align="center">Сборка из исходников</h1>

<p align="center">
  <a href="BUILDING.md">English</a> |
  <a href="BUILDING.ru.md">Русский</a>
</p>

## Что нужно

| Инструмент | Версия |
| --- | --- |
| Visual Studio 2022 | рабочая нагрузка «Разработка приложений для универсальной платформы Windows» |
| Windows SDK | 10.0.22621 |
| .NET SDK | 8.0 |
| .NET Framework | 4.8 (установщик и деинсталлятор) |

Виджет — это UWP-приложение, собираемое .NET Native, поэтому его нужно собирать MSBuild из Visual Studio, а не через `dotnet build`.

## Всё сразу

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```

Скрипт публикует панель как self-contained single file, собирает виджет и деинсталлятор, упаковывает их в `installer\VlcControl.Setup\payload.zip` и делает `artifacts\dist\VLCControl-Setup.exe`.

## Отдельные части

```powershell
# только панель (self-contained, single file)
dotnet publish VlcControl\VlcControl.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o VlcControl\publish

# виджет + лаунчер (MSBuild, .NET Native)
powershell -ExecutionPolicy Bypass -File tools\build-widget.ps1

# регистрация пакета виджета и loopback-исключение
powershell -ExecutionPolicy Bypass -File tools\install-widget.ps1

# настройка HTTP-интерфейса VLC (vlcrc)
powershell -ExecutionPolicy Bypass -File tools\setup-vlc.ps1
```

> Сборка виджета **всегда** начинается с удаления `obj` и `bin`. Инкрементальная сборка .NET Native периодически оставляет битые артефакты: пакет после этого регистрируется, но молча не запускается (ошибка `TWinUI 5961`, «Приложение не запустилось»).

## Структура проекта

```
VlcControl/                  панель (WPF, .NET 8, self-contained single file)
  MainWindow.xaml(.cs)       тёмный интерфейс панели
  Services/VlcController.cs  логика плеера, плейлист, автозапуск VLC
  VlcClient.cs               HTTP-клиент VLC
  Services/Lang.cs           определение и переключение языка
  Resources/Strings.*.xaml   таблицы строк RU/EN
installer/
  Shared/Lang.cs             общие для установщика и деинсталлятора помощники языка
  VlcControl.Setup/          установщик (WPF, .NET Framework 4.8, payload внутри)
  VlcControl.Uninstall/      деинсталлятор
src/
  VlcGameBarWidget/          виджет Game Bar (UWP)
    UiStrings.cs             таблица строк RU/EN
  VlcLauncher/               лаунчер, стартует VLC с HTTP
tools/                       скрипты сборки и установки
```

## Ручная установка виджета

Если не хочется пользоваться установщиком:

1. Включить режим разработчика: **Параметры → Система → Для разработчиков**.
2. Настроить HTTP-интерфейс VLC: **Инструменты → Настройки → Все → Интерфейс → Основные интерфейсы → Web** (хост `127.0.0.1`, порт `8080`, пароль `gamebar`).
3. Зарегистрировать пакет: `Add-AppxPackage -Register src\VlcGameBarWidget\bin\x64\Release\ilc\AppxManifest.xml`.
4. Выдать loopback-исключение: `CheckNetIsolation LoopbackExempt -a -n=vlcgamebarwidget_11mkq6sn63zaj`.
5. `Win+G` → иконка сетки виджетов → **VLC Control** → закрепить.

## Язык

Установщик, деинсталлятор и панель используют WPF-словари ресурсов (`Resources\Strings.ru.xaml`, `Strings.en.xaml`) с подстановкой `{DynamicResource}`. У виджета нет поддержки словарей ресурсов, поэтому в нём обычная таблица строк в `UiStrings.cs`.

Выбранный язык хранится в `lang.txt`:

- `%LOCALAPPDATA%\Programs\VLCControl\lang.txt` — читают панель и установщик;
- `%LOCALAPPDATA%\Programs\VLCControl\widget\lang.txt` — читает виджет через папку установки;
- `%LOCALAPPDATA%\Packages\vlcgamebarwidget_11mkq6sn63zaj\LocalState\lang.txt` — запасной вариант для виджета.

Виджет дополнительно хранит собственное переопределение в `ApplicationData.Current.LocalSettings["UiLanguage"]` — оно важнее файлов.
