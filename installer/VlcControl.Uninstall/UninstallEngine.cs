using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace VlcControl.Uninstall
{
    public sealed class UninstallEngine
    {
        public const string PackageName = "VlcGameBarWidget";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VLC Control";

        private readonly StringBuilder _log = new StringBuilder();

        public event Action<string> Log;

        public string InstallDir { get; set; }

        public void Run()
        {
            StopProcesses();

            var family = ReadPackageFamilyName();
            RemovePackage();
            RemoveLoopback(family);
            RestoreDeveloperMode();
            DeleteShortcuts();
            DeleteUninstallEntry();
            WriteLog();
        }

        public void DeleteInstallDirectory()
        {
            var source = Assembly.GetExecutingAssembly().Location;
            var temp = Path.Combine(Path.GetTempPath(), "VlcControl-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.Copy(source, temp, true);
                var psi = new ProcessStartInfo(temp, "/cleanup \"" + InstallDir + "\" /self \"" + temp + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogLine("не удалось запустить очистку папки: " + ex.Message);
            }
        }

        public void DeleteDirectoryNow(string directory, string selfPath)
        {
            Thread.Sleep(1200);
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception ex)
            {
                LogLine("не удалось удалить папку: " + ex.Message);
            }

            try
            {
                var cmd = "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + selfPath + "\"";
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch
            {
            }
        }

        private void StopProcesses()
        {
            foreach (var name in new[] { "GameBar", "GameBarFTServer", "VlcGameBarWidget", "VlcControl" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(4000);
                    }
                    catch
                    {
                    }
                }
            }

            LogLine("остановлены запущенные компоненты");
        }

        private string ReadPackageFamilyName()
        {
            try
            {
                var text = RunPowerShell("(Get-AppxPackage -Name " + PackageName + ").PackageFamilyName");
                var value = text.Trim();
                return value.Length == 0 ? "vlcgamebarwidget_11mkq6sn63zaj" : value;
            }
            catch
            {
                return "vlcgamebarwidget_11mkq6sn63zaj";
            }
        }

        private void RemovePackage()
        {
            RunPowerShell("Get-AppxPackage -Name " + PackageName + " | Remove-AppxPackage -ErrorAction SilentlyContinue");
            LogLine("пакет виджета удалён");
        }

        private void RemoveLoopback(string family)
        {
            var tool = Path.Combine(Environment.SystemDirectory, "CheckNetIsolation.exe");
            if (!File.Exists(tool))
            {
                return;
            }

            int code;
            RunProcess(tool, "LoopbackExempt -d -n=" + family, out code);
            LogLine("исключение локальной сети снято");
        }

        private void RestoreDeveloperMode()
        {
            var stateFile = Path.Combine(InstallDir ?? string.Empty, "install-state.ini");
            if (!File.Exists(stateFile))
            {
                return;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(stateFile))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
            try
            {
                using (var handle = Registry.LocalMachine.OpenSubKey(key, true))
                {
                    if (handle == null)
                    {
                        return;
                    }

                    ApplyValue(handle, "AllowDevelopmentWithoutDevLicense", values.ContainsKey("allowDev") ? values["allowDev"] : "-1");
                    ApplyValue(handle, "AllowAllTrustedApps", values.ContainsKey("allowTrusted") ? values["allowTrusted"] : "-1");
                }

                LogLine("режим разработчика восстановлен");
            }
            catch (Exception ex)
            {
                LogLine("не удалось восстановить режим разработчика: " + ex.Message);
            }
        }

        private static void ApplyValue(RegistryKey key, string name, string raw)
        {
            int value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                if (key.GetValue(name) != null)
                {
                    key.DeleteValue(name, false);
                }

                return;
            }

            key.SetValue(name, value, RegistryValueKind.DWord);
        }

        private void DeleteShortcuts()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

            foreach (var path in new[]
            {
                Path.Combine(desktop, "VLC Control.lnk"),
                Path.Combine(programs, "VLC Control.lnk")
            })
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                }
            }

            LogLine("ярлыки удалены");
        }

        private void DeleteUninstallEntry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                LogLine("запись в «Программы и компоненты» удалена");
            }
            catch (Exception ex)
            {
                LogLine("не удалось удалить запись: " + ex.Message);
            }
        }

        private void LogLine(string text)
        {
            _log.AppendLine(DateTime.Now.ToString("HH:mm:ss ") + text);
            var handler = Log;
            if (handler != null)
            {
                handler(text);
            }
        }

        private void WriteLog()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VlcControl");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "uninstall.log"), _log.ToString());
            }
            catch
            {
            }
        }

        private static string RunPowerShell(string command)
        {
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            int code;
            return RunProcess(shell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"", out code);
        }

        private static string RunProcess(string fileName, string arguments, out int exitCode)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output;
            string error;
            using (var process = Process.Start(psi))
            {
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit(120000);
                exitCode = process.ExitCode;
            }

            return output + error;
        }
    }
}
