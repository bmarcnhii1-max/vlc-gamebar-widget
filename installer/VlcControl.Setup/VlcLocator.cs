using System;
using System.IO;
using Microsoft.Win32;

namespace VlcControl.Setup
{
    internal static class VlcLocator
    {
        public static string Find()
        {
            foreach (var candidate in Candidates())
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string[] Candidates()
        {
            var paths = new System.Collections.Generic.List<string>();
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (programFiles.Length > 0)
            {
                paths.Add(Path.Combine(programFiles, "VideoLAN", "VLC", "vlc.exe"));
            }

            if (programFilesX86.Length > 0)
            {
                paths.Add(Path.Combine(programFilesX86, "VideoLAN", "VLC", "vlc.exe"));
            }

            paths.Add(Path.Combine(localAppData, "Programs", "VideoLAN", "VLC", "vlc.exe"));
            paths.Add(Path.Combine(localAppData, "Programs", "VLC", "vlc.exe"));

            foreach (var key in new[] { "SOFTWARE\\VideoLAN\\VLC", "SOFTWARE\\WOW6432Node\\VideoLAN\\VLC" })
            {
                try
                {
                    using (var registry = Registry.LocalMachine.OpenSubKey(key))
                    {
                        var install = registry?.GetValue("InstallDir") as string;
                        if (!string.IsNullOrEmpty(install))
                        {
                            paths.Add(Path.Combine(install.Trim(), "vlc.exe"));
                        }
                    }
                }
                catch
                {
                }
            }

            return paths.ToArray();
        }
    }
}
