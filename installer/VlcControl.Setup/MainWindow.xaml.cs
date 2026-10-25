using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VlcControl.Install;

namespace VlcControl.Setup
{
    public partial class MainWindow : Window
    {
        private readonly string _installDir;
        private readonly List<StepRow> _rows = new List<StepRow>();
        private SetupEngine _engine;

        public MainWindow(string installDir)
        {
            InitializeComponent();
            _installDir = installDir;

            Title = Lang.T("WindowTitle");
            InstallPathText.Text = Lang.T("InstallingTo") + _installDir;
            UpdateLanguageButtons();
            DetectVlc();
            BuildSteps();
        }

        private void DetectVlc()
        {
            var vlc = VlcLocator.Find();
            if (vlc == null)
            {
                VlcDot.Fill = (Brush)FindResource("RedBrush");
                VlcStatusText.Text = Lang.T("VlcNotFound");
                VlcPathText.Text = Lang.T("VlcNotFoundHint");
                DownloadVlcButton.Visibility = Visibility.Visible;
            }
            else
            {
                VlcDot.Fill = (Brush)FindResource("GreenBrush");
                VlcStatusText.Text = Lang.T("VlcFound");
                VlcPathText.Text = vlc;
                DownloadVlcButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateLanguageButtons()
        {
            LangRuButton.Foreground = Lang.Current == UiLanguage.Ru
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush");
            LangEnButton.Foreground = Lang.Current == UiLanguage.En
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush");
        }

        private void LangRu_Click(object sender, RoutedEventArgs e) => ApplyLanguage(UiLanguage.Ru);

        private void LangEn_Click(object sender, RoutedEventArgs e) => ApplyLanguage(UiLanguage.En);

        private void ApplyLanguage(UiLanguage language)
        {
            Lang.Apply(language);

            Title = Lang.T("WindowTitle");
            InstallPathText.Text = Lang.T("InstallingTo") + _installDir;
            UpdateLanguageButtons();
            DetectVlc();

            StepsPanel.Children.Clear();
            _rows.Clear();
            BuildSteps();
        }

        private void BuildSteps()
        {
            var engine = new SetupEngine(_installDir);
            for (var i = 0; i < engine.Steps.Count; i++)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var glyph = new TextBlock
                {
                    Width = 18,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    Text = "•",
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };

                var title = new TextBlock
                {
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextBrush"),
                    Text = engine.Steps[i].Title,
                    TextWrapping = TextWrapping.Wrap
                };

                var detail = new TextBlock
                {
                    Margin = new Thickness(0, 2, 0, 0),
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    Text = string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                    Visibility = Visibility.Collapsed
                };

                var text = new StackPanel();
                text.Children.Add(title);
                text.Children.Add(detail);

                Grid.SetColumn(glyph, 0);
                Grid.SetColumn(text, 1);
                grid.Children.Add(glyph);
                grid.Children.Add(text);

                var row = new StepRow { Glyph = glyph, Title = title, Detail = detail };
                _rows.Add(row);
                StepsPanel.Children.Add(grid);

                if (i < _rows.Count - 1)
                {
                    StepsPanel.Children.Add(new Border
                    {
                        Height = 10,
                        Width = 0
                    });
                }
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            await StartInstallAsync();
        }

        private async Task StartInstallAsync()
        {
            ShowPanel(ProgressPanel);
            SetStepsPending();

            _engine = new SetupEngine(_installDir);
            _engine.StepChanged += index => Dispatcher.InvokeAsync(() => OnStepChanged(index));
            _engine.Log += text => Dispatcher.InvokeAsync(() => { LogText.Text = text; });

            var result = await Task.Run(() => _engine.Run());

            if (result.Success)
            {
                OnInstallFinished(result);
            }
            else
            {
                ErrorText.Text = result.Error ?? Lang.T("UnknownError");
                ShowPanel(ErrorPanel);
            }
        }

        private void OnStepChanged(int index)
        {
            for (var i = 0; i < _engine.Steps.Count; i++)
            {
                var step = _engine.Steps[i];
                var row = _rows[i];

                string glyph;
                Brush color;

                switch (step.Status)
                {
                    case StepStatus.Ok:
                        glyph = "✓";
                        color = (Brush)FindResource("GreenBrush");
                        break;
                    case StepStatus.Warn:
                        glyph = "!";
                        color = (Brush)FindResource("AccentBrush");
                        break;
                    case StepStatus.Error:
                        glyph = "✕";
                        color = (Brush)FindResource("RedBrush");
                        break;
                    case StepStatus.Running:
                        glyph = "•";
                        color = (Brush)FindResource("AccentBrush");
                        break;
                    default:
                        glyph = "•";
                        color = (Brush)FindResource("MutedBrush");
                        break;
                }

                row.Glyph.Text = glyph;
                row.Glyph.Foreground = color;
                row.Title.Foreground = step.Status == StepStatus.Pending
                    ? (Brush)FindResource("MutedBrush")
                    : (Brush)FindResource("TextBrush");

                if (string.IsNullOrEmpty(step.Detail))
                {
                    row.Detail.Visibility = Visibility.Collapsed;
                }
                else
                {
                    row.Detail.Text = step.Detail;
                    row.Detail.Visibility = Visibility.Visible;
                }

                if (step.Status == StepStatus.Running)
                {
                    StatusText.Text = step.Title + " — " + (step.Detail ?? string.Empty);
                }
            }

            var done = 0;
            foreach (var step in _engine.Steps)
            {
                if (step.Status == StepStatus.Ok || step.Status == StepStatus.Warn)
                {
                    done++;
                }
            }

            ProgressBar.Value = done * 100.0 / _engine.Steps.Count;
        }

        private void SetStepsPending()
        {
            foreach (var row in _rows)
            {
                row.Glyph.Text = "•";
                row.Glyph.Foreground = (Brush)FindResource("MutedBrush");
                row.Title.Foreground = (Brush)FindResource("MutedBrush");
                row.Detail.Visibility = Visibility.Collapsed;
            }

            StatusText.Text = Lang.T("Preparing");
            LogText.Text = string.Empty;
            ProgressBar.Value = 0;
        }

        private void OnInstallFinished(SetupResult result)
        {
            var warnings = new List<string>();

            if (!result.WidgetInstalled)
            {
                warnings.Add(Lang.T("WidgetFailed") + result.WidgetMessage);
            }

            if (result.VlcWasRunning)
            {
                warnings.Add(Lang.T("VlcRunningWarning"));
            }

            if (warnings.Count > 0)
            {
                FinishWarningText.Text = string.Join("\n", warnings);
                FinishWarningText.Visibility = Visibility.Visible;
            }

            ShowPanel(FinishPanel);
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await StartInstallAsync();
        }

        private void ShowPanel(FrameworkElement panel)
        {
            WelcomePanel.Visibility = panel == WelcomePanel ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanel.Visibility = panel == ProgressPanel ? Visibility.Visible : Visibility.Collapsed;
            FinishPanel.Visibility = panel == FinishPanel ? Visibility.Visible : Visibility.Collapsed;
            ErrorPanel.Visibility = panel == ErrorPanel ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.IsEnabled = panel != ProgressPanel;
        }

        private void OpenPanelButton_Click(object sender, RoutedEventArgs e)
        {
            var exe = Path.Combine(_installDir, "app", SetupEngine.AppExeName);
            if (!File.Exists(exe))
            {
                MessageBox.Show(Lang.T("PanelMissing") + exe, "VLC Control", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }

            Application.Current.Shutdown();
        }

        private void DownloadVlc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.videolan.org/vlc/") { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
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

        private sealed class StepRow
        {
            public TextBlock Glyph { get; set; }

            public TextBlock Title { get; set; }

            public TextBlock Detail { get; set; }
        }
    }
}
