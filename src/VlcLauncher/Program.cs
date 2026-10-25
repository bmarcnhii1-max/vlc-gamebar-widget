using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace VlcLauncher
{
    internal static class Program
    {
        private const string Arguments =
            "--extraintf=http --http-host=127.0.0.1 --http-port=8080 --http-password=gamebar";

        [STAThread]
        private static void Main()
        {
            try
            {
                Log("start");

                if (HttpReady())
                {
                    Log("http already ready");
                    return;
                }

                if (Process.GetProcessesByName("vlc").Length > 0)
                {
                    Log("stopping vlc without http");
                    StopVlc();
                }

                var path = FindVlc();
                if (path == null)
                {
                    Log("vlc not found");
                    return;
                }

                Log("launching " + path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = Arguments,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        private static bool HttpReady()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8080/requests/status.xml");
                request.Proxy = null;
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;
                request.Headers["Authorization"] =
                    "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":gamebar"));

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void StopVlc()
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

            Thread.Sleep(1200);

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

            Thread.Sleep(500);
        }

        private static string FindVlc()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                Path.Combine(programFiles, "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(programFilesX86, "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(localAppData, "Programs", "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(localAppData, "VideoLAN", "VLC", "vlc.exe"),
                Path.Combine(baseDir, "vlc.exe"),
                Path.Combine(baseDir, "vlc", "vlc.exe"),
            };

            foreach (var candidate in candidates)
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

            foreach (var registryPath in new[]
                     {
                         @"SOFTWARE\VideoLAN\VLC",
                         @"SOFTWARE\WOW6432Node\VideoLAN\VLC",
                     })
            {
                foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using (var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(registryPath))
                        {
                            var installDir = key?.GetValue("InstallDir") as string;
                            if (!string.IsNullOrWhiteSpace(installDir))
                            {
                                var candidate = Path.Combine(installDir, "vlc.exe");
                                if (File.Exists(candidate))
                                {
                                    return candidate;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static void Log(string text)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "vlclauncher.log"),
                    DateTime.Now.ToString("O") + " " + text + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
