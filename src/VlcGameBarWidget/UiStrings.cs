using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Windows.ApplicationModel;
using Windows.Storage;

namespace VlcGameBarWidget
{
    internal enum UiLanguage
    {
        Ru,
        En
    }

    internal static class UiStrings
    {
        public const string LangFileName = "lang.txt";

        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "Connecting", "Подключение…" },
            { "Playlist", "Плейлист" },
            { "Settings", "Настройки" },
            { "NothingPlaying", "Ничего не воспроизводится" },
            { "Previous", "Предыдущий" },
            { "Back10", "Назад 10 секунд" },
            { "PlayPause", "Пауза / Играть" },
            { "Forward10", "Вперёд 10 секунд" },
            { "Next", "Следующий" },
            { "Mute", "Выключить звук" },
            { "Speed", "Скорость" },
            { "PlaylistEmpty", "Плейлист пуст" },
            { "OfflineTitle", "VLC не отвечает" },
            { "OfflineText", "Одна кнопка запустит VLC с нужными настройками. Если он уже открыт без HTTP — перезапустит." },
            { "LaunchVlc", "Запустить VLC" },
            { "Retry", "Проверить снова" },
            { "SettingsSubtitle", "HTTP-интерфейс VLC" },
            { "Host", "Адрес" },
            { "Port", "Порт" },
            { "Password", "Пароль" },
            { "Check", "Проверить" },
            { "Cancel", "Отмена" },
            { "Save", "Сохранить" },
            { "Language", "Язык" },
            { "StatePlaying", "Играет" },
            { "StatePaused", "Пауза" },
            { "StateStopped", "Стоп" },
            { "StateReady", "Готов" },
            { "NoConnection", "Нет связи" },
            { "PortInvalid", "Порт должен быть числом от 1 до 65535" },
            { "Checking", "Проверяю…" },
            { "ConnectionOk", "Связь есть" },
            { "ConnectionOkTitle", "Связь есть: " },
            { "NoConnectionPrefix", "Нет связи: " },
            { "Launching", "Запускаю VLC…" },
            { "LaunchFailed", "Не удалось запустить: " },
            { "LaunchNoAnswer", "VLC не ответил. Проверь, что он установлен, и нажми ещё раз." },
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "Connecting", "Connecting…" },
            { "Playlist", "Playlist" },
            { "Settings", "Settings" },
            { "NothingPlaying", "Nothing is playing" },
            { "Previous", "Previous" },
            { "Back10", "Back 10 seconds" },
            { "PlayPause", "Play / Pause" },
            { "Forward10", "Forward 10 seconds" },
            { "Next", "Next" },
            { "Mute", "Mute" },
            { "Speed", "Speed" },
            { "PlaylistEmpty", "Playlist is empty" },
            { "OfflineTitle", "VLC is not responding" },
            { "OfflineText", "One button starts VLC with the right settings. If it is already open without HTTP, it will restart it." },
            { "LaunchVlc", "Launch VLC" },
            { "Retry", "Try again" },
            { "SettingsSubtitle", "VLC HTTP interface" },
            { "Host", "Host" },
            { "Port", "Port" },
            { "Password", "Password" },
            { "Check", "Test" },
            { "Cancel", "Cancel" },
            { "Save", "Save" },
            { "Language", "Language" },
            { "StatePlaying", "Playing" },
            { "StatePaused", "Paused" },
            { "StateStopped", "Stopped" },
            { "StateReady", "Ready" },
            { "NoConnection", "No connection" },
            { "PortInvalid", "Port must be a number from 1 to 65535" },
            { "Checking", "Checking…" },
            { "ConnectionOk", "Connection OK" },
            { "ConnectionOkTitle", "Connected: " },
            { "NoConnectionPrefix", "No connection: " },
            { "Launching", "Launching VLC…" },
            { "LaunchFailed", "Failed to launch: " },
            { "LaunchNoAnswer", "VLC did not respond. Make sure it is installed and press the button again." },
        };

        private static Dictionary<string, string> _current = Ru;

        public static UiLanguage Language { get; private set; } = UiLanguage.Ru;

        public static string T(string key)
        {
            string value;
            return _current.TryGetValue(key, out value) ? value : key;
        }

        public static void Load()
        {
            Language = Detect();
            _current = Language == UiLanguage.En ? En : Ru;
        }

        public static void SetLanguage(UiLanguage language)
        {
            Language = language;
            _current = language == UiLanguage.En ? En : Ru;

            try
            {
                ApplicationData.Current.LocalSettings.Values["UiLanguage"] = language == UiLanguage.En ? "en" : "ru";
            }
            catch
            {
            }
        }

        private static UiLanguage Detect()
        {
            var stored = ReadOverride();
            if (stored.HasValue)
            {
                return stored.Value;
            }

            foreach (var path in LanguageFiles())
            {
                var code = ReadCode(path);
                if (code != null)
                {
                    return Parse(code);
                }
            }

            var culture = CultureInfo.CurrentUICulture != null ? CultureInfo.CurrentUICulture.Name : string.Empty;
            return culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Ru : UiLanguage.En;
        }

        private static UiLanguage? ReadOverride()
        {
            try
            {
                object stored;
                if (!ApplicationData.Current.LocalSettings.Values.TryGetValue("UiLanguage", out stored))
                {
                    return null;
                }

                var code = stored as string;
                if (string.IsNullOrEmpty(code))
                {
                    return null;
                }

                return Parse(code);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> LanguageFiles()
        {
            var paths = new List<string>();

            try
            {
                paths.Add(Path.Combine(Package.Current.InstalledLocation.Path, LangFileName));
            }
            catch
            {
            }

            try
            {
                paths.Add(Path.Combine(ApplicationData.Current.LocalFolder.Path, LangFileName));
            }
            catch
            {
            }

            return paths;
        }

        private static string ReadCode(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var text = File.ReadAllText(path).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static UiLanguage Parse(string code)
        {
            return code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.En : UiLanguage.Ru;
        }
    }
}
