using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;

namespace VlcControl.Install
{
    internal enum UiLanguage
    {
        Ru,
        En
    }

    internal static class Lang
    {
        public const string LanguageFileName = "lang.txt";
        public const string WidgetPackageFamily = "VlcGameBarWidget_11mkq6sn63zaj";

        public static UiLanguage Current { get; private set; } = UiLanguage.Ru;

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "VLCControl");

        public static void Load()
        {
            Apply(Detect());
        }

        public static void Apply(UiLanguage language)
        {
            Current = language;

            var app = Application.Current;
            if (app == null) return;

            var dictionary = new ResourceDictionary
            {
                Source = new Uri("Resources/Strings." + Code(language) + ".xaml", UriKind.Relative)
            };

            var merged = app.Resources.MergedDictionaries;
            var index = -1;
            for (var i = 0; i < merged.Count; i++)
            {
                var source = merged[i].Source != null ? merged[i].Source.OriginalString : string.Empty;
                if (source.IndexOf("Strings.", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                merged[index] = dictionary;
            }
            else
            {
                merged.Add(dictionary);
            }

            Snapshot(dictionary);
        }

        private static void Snapshot(ResourceDictionary dictionary)
        {
            Strings.Clear();
            foreach (var key in dictionary.Keys)
            {
                var name = key as string;
                if (name == null) continue;

                var value = dictionary[name] as string;
                if (value != null) Strings[name] = value;
            }
        }

        public static string T(string key)
        {
            string value;
            return Strings.TryGetValue(key, out value) ? value : key;
        }

        public static string Code(UiLanguage language) => language == UiLanguage.En ? "en" : "ru";

        public static void Save(UiLanguage language, string installDir)
        {
            var code = Code(language);

            if (!string.IsNullOrEmpty(installDir))
            {
                TryWrite(Path.Combine(installDir, LanguageFileName), code, false);

                var widgetDir = Path.Combine(installDir, "widget");
                if (Directory.Exists(widgetDir))
                {
                    TryWrite(Path.Combine(widgetDir, LanguageFileName), code, false);
                }
            }

            var localState = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                WidgetPackageFamily,
                "LocalState");
            TryWrite(Path.Combine(localState, LanguageFileName), code, true);
        }

        private static void TryWrite(string path, string code, bool createDirectory)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        if (!createDirectory)
                        {
                            return;
                        }

                        Directory.CreateDirectory(directory);
                    }
                }

                File.WriteAllText(path, code);
            }
            catch
            {
            }
        }

        private static UiLanguage Detect()
        {
            foreach (var path in LanguageFiles())
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    var text = File.ReadAllText(path).Trim();
                    if (text.Length == 0) continue;

                    return text.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.En : UiLanguage.Ru;
                }
                catch
                {
                }
            }

            var culture = CultureInfo.CurrentUICulture != null ? CultureInfo.CurrentUICulture.Name : string.Empty;
            return culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Ru : UiLanguage.En;
        }

        private static IEnumerable<string> LanguageFiles()
        {
            yield return Path.Combine(InstallDir, LanguageFileName);
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LanguageFileName);
        }
    }
}
