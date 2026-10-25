using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VlcControl
{
    internal static class VlcController
    {
        public const string LaunchArguments =
            "--extraintf=http --http-host=127.0.0.1 --http-port=8080 --http-password=gamebar";

        public static string FindVlc()
        {
            foreach (var candidate in EnumerateCandidates())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = AppContext.BaseDirectory;

            yield return Path.Combine(programFiles, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(programFilesX86, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(localAppData, "Programs", "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(localAppData, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(baseDir, "vlc.exe");
            yield return Path.Combine(baseDir, "vlc", "vlc.exe");
            yield return Path.Combine(baseDir, "VLC", "vlc.exe");

            foreach (var registryPath in new[]
                     {
                         @"SOFTWARE\VideoLAN\VLC",
                         @"SOFTWARE\WOW6432Node\VideoLAN\VLC",
                     })
            {
                foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    {
                        string installDir = null;
                        try
                        {
                            using (var root = RegistryKey.OpenBaseKey(hive, view))
                            using (var key = root.OpenSubKey(registryPath))
                            {
                                installDir = key?.GetValue("InstallDir") as string;
                            }
                        }
                        catch
                        {
                        }

                        if (!string.IsNullOrWhiteSpace(installDir))
                        {
                            yield return Path.Combine(installDir, "vlc.exe");
                        }
                    }
                }
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = null;
                try
                {
                    candidate = Path.Combine(directory.Trim(), "vlc.exe");
                }
                catch
                {
                }

                if (candidate != null)
                {
                    yield return candidate;
                }
            }
        }

        public static bool IsRunning()
        {
            try
            {
                return Process.GetProcessesByName("vlc").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void Launch(string vlcPath)
        {
            var info = new ProcessStartInfo
            {
                FileName = vlcPath,
                Arguments = LaunchArguments,
                WorkingDirectory = Path.GetDirectoryName(vlcPath) ?? string.Empty,
                UseShellExecute = false,
            };

            Process.Start(info);
        }

        public static async Task RestartAsync(string vlcPath)
        {
            foreach (var process in Process.GetProcessesByName("vlc"))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                    }
                }
                catch
                {
                }
            }

            await Task.Delay(1200).ConfigureAwait(false);

            foreach (var process in Process.GetProcessesByName("vlc"))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                }
            }

            await Task.Delay(600).ConfigureAwait(false);
            Launch(vlcPath);
        }
    }
}
