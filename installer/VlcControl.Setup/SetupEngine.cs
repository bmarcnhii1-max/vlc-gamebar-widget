using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace VlcControl.Setup
{
    public enum StepStatus
    {
        Pending,
        Running,
        Ok,
        Warn,
        Error
    }

    public sealed class StepInfo
    {
        public StepInfo(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public StepStatus Status { get; set; }

        public string Detail { get; set; }
    }

    public sealed class SetupResult
    {
        public bool Success { get; set; }

        public string Error { get; set; }

        public bool WidgetInstalled { get; set; }

        public string WidgetMessage { get; set; }

        public bool VlcConfigured { get; set; }

        public string VlcMessage { get; set; }

        public bool ShortcutsCreated { get; set; }

        public string InstallDir { get; set; }
    }

    public sealed class SetupEngine
    {
        public const string PackageName = "VlcGameBarWidget";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VLC Control";
        public const string AppExeName = "VlcControl.exe";

        private readonly string _installDir;
        private readonly List<StepInfo> _steps = new List<StepInfo>();
        private readonly StringBuilder _log = new StringBuilder();

        public SetupEngine(string installDir)
        {
            _installDir = installDir;
            _steps.Add(new StepInfo("Подготовка"));
            _steps.Add(new StepInfo("Копирование файлов"));
            _steps.Add(new StepInfo("Виджет для Xbox Game Bar"));
            _steps.Add(new StepInfo("Настройки VLC"));
            _steps.Add(new StepInfo("Ярлыки и удаление"));
        }

        public IList<StepInfo> Steps => _steps;

        public event Action<int> StepChanged;

        public event Action<string> Log;

        public string InstallDir => _installDir;

        public string AppExePath => Path.Combine(_installDir, "app", AppExeName);

        public SetupResult Run()
        {
            var result = new SetupResult { InstallDir = _installDir };

            try
            {
                Step(0, StepStatus.Running, "Останавливаю запущенные компоненты");
                Directory.CreateDirectory(_installDir);
                StopProcesses();
                Step(0, StepStatus.Ok, null);

                Step(1, StepStatus.Running, "Распаковываю панель и виджет");
                ExtractPayload();
                Step(1, StepStatus.Ok, null);

                Step(2, StepStatus.Running, "Регистрирую виджет");
                try
                {
                    result.WidgetMessage = InstallWidget();
                    result.WidgetInstalled = true;
                    Step(2, StepStatus.Ok, result.WidgetMessage);
                }
                catch (Exception ex)
                {
                    result.WidgetInstalled = false;
                    result.WidgetMessage = ex.Message;
                    Step(2, StepStatus.Warn, ex.Message);
                }

                Step(3, StepStatus.Running, "Настраиваю HTTP-интерфейс VLC");
                try
                {
                    result.VlcMessage = ConfigureVlc();
                    result.VlcConfigured = true;
                    Step(3, StepStatus.Ok, result.VlcMessage);
                }
                catch (Exception ex)
                {
                    result.VlcConfigured = false;
                    result.VlcMessage = ex.Message;
                    Step(3, StepStatus.Warn, ex.Message);
                }

                Step(4, StepStatus.Running, "Создаю ярлыки");
                CreateShortcuts();
                WriteUninstallEntry();
                result.ShortcutsCreated = true;
                Step(4, StepStatus.Ok, null);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                var running = _steps.FirstOrDefault(s => s.Status == StepStatus.Running);
                if (running != null)
                {
                    running.Status = StepStatus.Error;
                    running.Detail = ex.Message;
                    Raise(running);
                }

                WriteLogFile();
            }

            WriteLogFile();
            return result;
        }

        private void Step(int index, StepStatus status, string detail)
        {
            var step = _steps[index];
            step.Status = status;
            step.Detail = detail;
            Raise(step);
        }

        private void Raise(StepInfo step)
        {
            var handler = StepChanged;
            if (handler != null)
            {
                handler(_steps.IndexOf(step));
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

        private void WriteLogFile()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VlcControl");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "setup.log"), _log.ToString());
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
        }

        private void ExtractPayload()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Файл установки повреждён: нет встроенных данных (payload.zip).");
                }

                var temp = Path.Combine(Path.GetTempPath(), "vlccontrol-payload-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using (var file = File.Create(temp))
                    {
                        stream.CopyTo(file);
                    }

                    foreach (var folder in new[] { "app", "widget" })
                    {
                        var target = Path.Combine(_installDir, folder);
                        if (Directory.Exists(target))
                        {
                            TryDelete(target);
                        }
                    }

                    var uninstaller = Path.Combine(_installDir, "Uninstall.exe");
                    if (File.Exists(uninstaller))
                    {
                        File.Delete(uninstaller);
                    }

                    ZipFile.ExtractToDirectory(temp, _installDir);
                }
                finally
                {
                    try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
                }
            }

            if (!File.Exists(AppExePath))
            {
                throw new InvalidOperationException("Панель не распаковалась: нет файла " + AppExeName + ".");
            }

            LogLine("payload extracted to " + _installDir);
        }

        private static void TryDelete(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private string InstallWidget()
        {
            var manifest = Path.Combine(_installDir, "widget", "AppxManifest.xml");
            if (!File.Exists(manifest))
            {
                throw new InvalidOperationException("Нет файла манифеста виджета.");
            }

            var devMode = EnsureDeveloperMode();
            RunPowerShell("Get-AppxPackage -Name " + PackageName + " | Remove-AppxPackage -ErrorAction SilentlyContinue");
            var outText = RunPowerShell(
                "$ErrorActionPreference='Stop'; Add-AppxPackage -Register '" + manifest.Replace("'", "''") +
                "'; (Get-AppxPackage -Name " + PackageName + ").PackageFullName");

            var family = ReadPackageFamilyName();
            var loopback = EnsureLoopbackExemption(family);
            LogLine("widget registered: " + outText.Trim());
            LogLine("developer mode: " + devMode);
            LogLine("loopback: " + loopback);
            return "Виджет зарегистрирован" + (devMode == "включён" ? ", режим разработчика включён" : string.Empty);
        }

        private string EnsureDeveloperMode()
        {
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
            var stateFile = Path.Combine(_installDir, "install-state.ini");
            var firstInstall = !File.Exists(stateFile);

            int? allowDev = null;
            int? allowTrusted = null;
            var keyExisted = false;

            using (var keyHandle = Registry.LocalMachine.OpenSubKey(key))
            {
                keyExisted = keyHandle != null;
                if (keyHandle != null)
                {
                    allowDev = ReadDword(keyHandle, "AllowDevelopmentWithoutDevLicense");
                    allowTrusted = ReadDword(keyHandle, "AllowAllTrustedApps");
                }
            }

            if (firstInstall)
            {
                try
                {
                    File.WriteAllLines(stateFile, new[]
                    {
                        "devKeyExisted=" + (keyExisted ? "1" : "0"),
                        "allowDev=" + (allowDev.HasValue ? allowDev.Value.ToString(CultureInfo.InvariantCulture) : "-1"),
                        "allowTrusted=" + (allowTrusted.HasValue ? allowTrusted.Value.ToString(CultureInfo.InvariantCulture) : "-1")
                    });
                }
                catch
                {
                }
            }

            if (allowDev == 1 && allowTrusted == 1)
            {
                return "уже включён";
            }

            using (var keyHandle = Registry.LocalMachine.CreateSubKey(key))
            {
                if (keyHandle == null)
                {
                    throw new InvalidOperationException("Не удалось открыть ключ режима разработчика.");
                }

                keyHandle.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
                keyHandle.SetValue("AllowAllTrustedApps", 1, RegistryValueKind.DWord);
            }

            return "включён";
        }

        private static int? ReadDword(RegistryKey key, string name)
        {
            var value = key.GetValue(name);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
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

        private string EnsureLoopbackExemption(string family)
        {
            var tool = Path.Combine(Environment.SystemDirectory, "CheckNetIsolation.exe");
            if (!File.Exists(tool))
            {
                return "инструмент не найден";
            }

            int code;
            var list = RunProcess(tool, "LoopbackExempt -s", out code);
            if (list.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "уже разрешён";
            }

            var result = RunProcess(tool, "LoopbackExempt -a -n=" + family, out code);
            if (code != 0)
            {
                throw new InvalidOperationException("Не удалось разрешить локальную сеть для виджета: " + result.Trim());
            }

            return "разрешён";
        }

        private string ConfigureVlc()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var vlcDir = Path.Combine(appData, "vlc");
            var vlcrc = Path.Combine(vlcDir, "vlcrc");

            if (Process.GetProcessesByName("vlc").Length > 0)
            {
                return "VLC сейчас запущен — файл настроек не трогаем, панель запустит VLC с нужными параметрами";
            }

            Directory.CreateDirectory(vlcDir);
            var existed = File.Exists(vlcrc);
            var lines = existed ? File.ReadAllLines(vlcrc) : new string[0];

            if (existed)
            {
                var backup = Path.Combine(vlcDir, "vlcrc.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                File.Copy(vlcrc, backup, true);
                LogLine("vlcrc backup: " + backup);
            }

            var updated = ApplyVlcSettings(lines);
            File.WriteAllLines(vlcrc, updated, new UTF8Encoding(false));

            return existed
                ? "HTTP-интерфейс включён (резервная копия сохранена)"
                : "Создан файл настроек VLC с HTTP-интерфейсом";
        }

        private static string[] ApplyVlcSettings(string[] lines)
        {
            var result = new List<string>(lines);
            var keys = new[]
            {
                "http-host",
                "http-port",
                "http-password"
            };

            foreach (var key in keys)
            {
                SetValue(result, key, key == "http-port" ? "8080" : key == "http-host" ? "127.0.0.1" : "gamebar");
            }

            var extraIndex = FindKey(result, "extraintf");
            if (extraIndex < 0)
            {
                result.Add("extraintf=http");
            }
            else
            {
                var current = result[extraIndex].Substring(result[extraIndex].IndexOf('=') + 1).Trim();
                var parts = current.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                if (!parts.Any(p => string.Equals(p, "http", StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add("http");
                    result[extraIndex] = "extraintf=" + string.Join(",", parts);
                }
            }

            return result.ToArray();
        }

        private static int FindKey(List<string> lines, string key)
        {
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SetValue(List<string> lines, string key, string value)
        {
            var index = FindKey(lines, key);
            var line = key + "=" + value;
            if (index >= 0)
            {
                lines[index] = line;
            }
            else
            {
                lines.Add(line);
            }
        }

        private void CreateShortcuts()
        {
            var exe = AppExePath;
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Нет файла панели для ярлыка.", exe);
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var programs = Path.Combine(startMenu, "Programs");

            Directory.CreateDirectory(programs);

            var desktopLink = Path.Combine(desktop, "VLC Control.lnk");
            var menuLink = Path.Combine(programs, "VLC Control.lnk");
            Shortcut.Create(desktopLink, exe);
            Shortcut.Create(menuLink, exe);
            LogLine("shortcuts: " + desktopLink + " ; " + menuLink);
        }

        private void WriteUninstallEntry()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Не удалось создать запись для удаления.");
                }

                var uninstaller = Path.Combine(_installDir, "Uninstall.exe");
                key.SetValue("DisplayName", "VLC Control");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "VLC Control");
                key.SetValue("InstallLocation", _installDir);
                key.SetValue("DisplayIcon", AppExePath + ",0");
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /S");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSizeKb(), RegistryValueKind.DWord);
                key.SetValue("URLInfoAbout", "https://github.com/bmarcnhii1-max/vlc-gamebar-widget");
            }
        }

        private int EstimateSizeKb()
        {
            try
            {
                long total = 0;
                foreach (var file in Directory.GetFiles(_installDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                    }
                }

                return (int)(total / 1024);
            }
            catch
            {
                return 0;
            }
        }

        internal static string RunPowerShell(string command)
        {
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            int code;
            return RunProcess(shell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"", out code);
        }

        internal static string RunProcess(string fileName, string arguments, out int exitCode)
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
                process.WaitForExit(180000);
                exitCode = process.ExitCode;
            }

            if (!string.IsNullOrEmpty(error))
            {
                output += "\n" + error;
            }

            return output;
        }
    }
}
