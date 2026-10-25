using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace VlcControl.Uninstall
{
    public partial class MainWindow : Window
    {
        private readonly string _installDir;

        public MainWindow(string installDir)
        {
            InitializeComponent();
            _installDir = installDir;
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            StatusText.Text = "Удаляю…";

            var engine = new UninstallEngine { InstallDir = _installDir };
            engine.Log += text => Dispatcher.InvokeAsync(() => StatusText.Text = text);

            await Task.Run(() => engine.Run());

            StatusText.Text = "Готово. Папка будет удалена через секунду.";
            await Task.Delay(600);

            engine.DeleteInstallDirectory();
            Application.Current.Shutdown();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }
    }
}
