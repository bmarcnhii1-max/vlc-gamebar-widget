using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace VlcControl.Uninstall
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = e.Args ?? new string[0];
            var cleanupIndex = Array.FindIndex(args, a => string.Equals(a, "/cleanup", StringComparison.OrdinalIgnoreCase));
            var selfIndex = Array.FindIndex(args, a => string.Equals(a, "/self", StringComparison.OrdinalIgnoreCase));

            if (cleanupIndex >= 0)
            {
                var directory = cleanupIndex + 1 < args.Length ? args[cleanupIndex + 1].Trim('"') : string.Empty;
                var self = selfIndex + 1 < args.Length ? args[selfIndex + 1].Trim('"') : string.Empty;
                var engine = new UninstallEngine { InstallDir = directory };
                engine.DeleteDirectoryNow(directory, self);
                Shutdown(0);
                return;
            }

            var installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var silent = args.Any(a =>
                string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

            if (silent)
            {
                var engine = new UninstallEngine { InstallDir = installDir };
                engine.Run();
                engine.DeleteInstallDirectory();
                Shutdown(0);
                return;
            }

            var window = new MainWindow(installDir);
            MainWindow = window;
            window.Show();
        }
    }
}
