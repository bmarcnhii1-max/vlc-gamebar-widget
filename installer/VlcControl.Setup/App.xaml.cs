using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using VlcControl.Install;

namespace VlcControl.Setup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Lang.Load();

            DispatcherUnhandledException += (sender, args) =>
            {
                WriteError(args.Exception);
                MessageBox.Show(
                    Lang.T("ErrUnhandled") + args.Exception.Message,
                    "VLC Control",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
            };

            var silent = e.Args.Any(a =>
                string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

            var dirArgument = e.Args.FirstOrDefault(a =>
                a.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase));

            var installDir = dirArgument != null
                ? dirArgument.Substring(5).Trim('"')
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "VLCControl");

            if (silent)
            {
                RunSilent(installDir);
                return;
            }

            var window = new MainWindow(installDir);
            MainWindow = window;
            window.Show();
        }

        private void RunSilent(string installDir)
        {
            var exitCode = 0;
            try
            {
                var engine = new SetupEngine(installDir);
                var result = engine.Run();
                exitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                WriteError(ex);
                exitCode = 1;
            }

            Shutdown(exitCode);
        }

        internal static void WriteError(Exception exception)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VlcControl");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "setup-error.log"),
                    DateTime.Now.ToString("O") + " " + exception + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
