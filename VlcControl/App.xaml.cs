using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace VlcControl
{
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VlcControl",
            "error.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                Log(args.ExceptionObject as Exception);

            DispatcherUnhandledException += (sender, args) =>
            {
                Log(args.Exception);
                args.Handled = false;
            };

            base.OnStartup(e);
        }

        private static void Log(Exception exception)
        {
            if (exception == null) return;

            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(LogPath, $"{DateTime.Now:O}  {exception}\r\n\r\n");
            }
            catch
            {
            }
        }
    }
}
