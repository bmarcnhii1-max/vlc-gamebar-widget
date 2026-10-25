using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;

namespace VlcControl
{
    internal enum UiLanguage
    {
        Ru,
        En
    }

    internal static class Lang
    {
        public const string LanguageFileName = "lang.txt";

        public static UiLanguage Current { get; private set; } = UiLanguage.Ru;

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
            for (var i = 0; i < merged.Count; i++)
            {
                var source = merged[i].Source != null ? merged[i].Source.OriginalString : string.Empty;
                if (source.IndexOf("Strings.", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    merged[i] = dictionary;
                    return;
                }
            }

            merged.Add(dictionary);
        }

        public static string T(string key)
        {
            var value = Application.Current != null ? Application.Current.TryFindResource(key) as string : null;
            return value ?? key;
        }

        public static string Code(UiLanguage language) => language == UiLanguage.En ? "en" : "ru";

        public static void Save(UiLanguage language)
        {
            var code = Code(language);

            TryWrite(Path.Combine(InstallDir, LanguageFileName), code);

            var widgetDir = Path.Combine(InstallDir, "widget");
            if (Directory.Exists(widgetDir))
            {
                TryWrite(Path.Combine(widgetDir, LanguageFileName), code);
            }

            TryWrite(Path.Combine(AppContext.BaseDirectory, LanguageFileName), code);
        }

        private static void TryWrite(string path, string code)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    return;
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
            yield return Path.Combine(AppContext.BaseDirectory, LanguageFileName);
        }
    }
}
