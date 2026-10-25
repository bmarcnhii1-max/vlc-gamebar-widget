using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VlcControl
{
    public partial class MainWindow : Window
    {
        private static readonly double[] Rates = { 0.5, 0.75, 1, 1.25, 1.5, 2 };

        private readonly VlcClient _client = new VlcClient();
        private readonly ObservableCollection<VlcPlaylistItem> _playlist = new ObservableCollection<VlcPlaylistItem>();
        private readonly List<Button> _rateButtons = new List<Button>();
        private readonly AppSettings _settings;

        private CancellationTokenSource _cts;
        private VlcStatus _status;
        private string _playlistSignature = string.Empty;
        private int _lastNonZeroVolume = 256;
        private int _failures;
        private bool _loaded;
        private bool _connected;
        private bool _launching;
        private DateTime _launchStartedUtc;
        private bool _playlistVisible;
        private bool _updatingSeek;
        private bool _updatingVolume;
        private bool _seekHeld;
        private bool _volumeHeld;
        private double _lastSeekPercent = -1;
        private double _lastVolumePercent = -1;
        private DateTime _lastSeekCommitUtc = DateTime.MinValue;
        private DateTime _lastVolumeCommitUtc = DateTime.MinValue;
        private DateTime _lastUserSeekUtc = DateTime.MinValue;
        private DateTime _lastUserVolumeUtc = DateTime.MinValue;
        private bool _closing;

        private Brush AccentBrush => (Brush)FindResource("Accent");
        private Brush MutedBrush => (Brush)FindResource("MutedBrush");
        private Brush ChipBrush => (Brush)FindResource("ChipBrush");
        private Brush GreenBrush => (Brush)FindResource("GreenBrush");
        private Brush RedBrush => (Brush)FindResource("RedBrush");

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _client.Configure(_settings.Host, _settings.Port, _settings.Password);

            Topmost = _settings.Topmost;
            UpdatePinVisual();

            BuildRateButtons();
            UpdateRateButtons(1);

            PlaylistList.ItemsSource = _playlist;
            LaunchOnStartBox.IsChecked = _settings.LaunchVlcOnStart;

            ConnText.Text = Lang.T("Connecting");
            TitleText.Text = Lang.T("NothingPlaying");
            UpdateLanguageButtons();

            ApplyInitialPosition();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void ApplyInitialPosition()
        {
            var area = SystemParameters.WorkArea;
            var left = _settings.Left ?? (area.Right - Width - 24);
            var top = _settings.Top ?? (area.Top + 80);

            if (double.IsNaN(left) || left < area.Left - 40 || left > area.Right - 80)
            {
                left = area.Right - Width - 24;
            }

            if (double.IsNaN(top) || top < area.Top - 10 || top > area.Bottom - 120)
            {
                top = area.Top + 80;
            }

            Left = left;
            Top = top;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            StartPolling();

            if (_settings.LaunchVlcOnStart && !VlcController.IsRunning())
            {
                var path = VlcController.FindVlc();
                if (path != null)
                {
                    _ = StartVlcAsync(path, restart: false);
                }
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _closing = true;
            _cts?.Cancel();
            _cts = null;

            if (WindowState == WindowState.Normal)
            {
                _settings.Left = Left;
                _settings.Top = Top;
            }

            _settings.Topmost = Topmost;
            _settings.LaunchVlcOnStart = LaunchOnStartBox.IsChecked == true;
            _settings.Save();

            _client.Dispose();
        }

        private void BuildRateButtons()
        {
            foreach (var rate in Rates)
            {
                var button = new Button
                {
                    Content = rate.ToString("0.##", CultureInfo.InvariantCulture) + "×",
                    Tag = rate,
                    Style = (Style)FindResource("ChipButton"),
                };
                button.Click += Rate_Click;
                RatePanel.Children.Add(button);
                _rateButtons.Add(button);
            }
        }

        private void StartPolling()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = PollLoopAsync(token);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            var tick = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var status = await _client.GetStatusAsync(token).ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() => ApplyStatus(status));

                    if (_playlistVisible && tick % 3 == 0)
                    {
                        var items = await _client.GetPlaylistAsync(token).ConfigureAwait(false);
                        await Dispatcher.InvokeAsync(() => ApplyPlaylist(items));
                    }

                    tick++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    await Dispatcher.InvokeAsync(HandlePollFailure);
                }

                try
                {
                    var delay = _launching && !_connected ? 550 : 900;
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }

        private void ApplyStatus(VlcStatus status)
        {
            if (_closing) return;

            _status = status;
            _failures = 0;
            _connected = true;
            _launching = false;

            if (OfflineCard.Visibility == Visibility.Visible)
            {
                OfflineCard.Visibility = Visibility.Collapsed;
                MainPanel.Visibility = Visibility.Visible;
            }

            Dot.Fill = GreenBrush;
            ConnText.Text = status.State == "playing" ? Lang.T("StatePlaying")
                : status.State == "paused" ? Lang.T("StatePaused")
                : status.State == "stopped" ? Lang.T("StateStopped")
                : Lang.T("StateReady");

            TitleText.Text = string.IsNullOrWhiteSpace(status.DisplayTitle)
                ? Lang.T("NothingPlaying")
                : status.DisplayTitle;

            var subtitle = BuildSubtitle(status);
            SubText.Text = subtitle;
            SubText.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;

            PlayIcon.Text = status.State == "playing" ? "\uE769" : "\uE768";

            if (!_seekHeld && DateTime.UtcNow - _lastUserSeekUtc > TimeSpan.FromSeconds(1.2))
            {
                _updatingSeek = true;
                SeekSlider.Maximum = Math.Max(1, status.Length);
                SeekSlider.Value = Math.Max(0, Math.Min(status.Time, SeekSlider.Maximum));
                TimeNow.Text = FormatTime(SeekSlider.Value);
                _updatingSeek = false;
            }

            if (!_seekHeld)
            {
                TimeNow.Text = FormatTime(status.Time);
            }

            TimeTotal.Text = FormatTime(status.Length);
            SeekSlider.IsEnabled = status.HasMedia;
            SeekSlider.Opacity = status.HasMedia ? 1 : 0.35;

            UpdateRateButtons(status.Rate);

            if (!_volumeHeld && DateTime.UtcNow - _lastUserVolumeUtc > TimeSpan.FromSeconds(1.2))
            {
                _updatingVolume = true;
                VolumeSlider.Value = Math.Max(0, Math.Min(100, status.Volume / 2.56));
                _updatingVolume = false;
            }

            VolumeText.Text = VolumePercent(status.Volume) + "%";
            if (status.Volume > 0)
            {
                _lastNonZeroVolume = status.Volume;
            }

            MuteIcon.Text = status.Volume == 0 ? "\uE74F" : "\uE767";
        }

        private void ApplyPlaylist(List<VlcPlaylistItem> items)
        {
            if (_closing) return;

            var signature = string.Join("|", items.Select(item => item.Id + ":" + item.Name));
            if (signature != _playlistSignature)
            {
                _playlistSignature = signature;
                _playlist.Clear();
                foreach (var item in items)
                {
                    _playlist.Add(item);
                }
            }
            else
            {
                for (var i = 0; i < items.Count && i < _playlist.Count; i++)
                {
                    _playlist[i].IsCurrent = items[i].IsCurrent;
                }
            }

            PlaylistCount.Text = items.Count.ToString(CultureInfo.InvariantCulture);
            PlaylistList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PlaylistEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HandlePollFailure()
        {
            if (_closing) return;

            _failures++;
            _connected = false;
            Dot.Fill = RedBrush;
            ConnText.Text = Lang.T("NoConnection");

            if (_launching)
            {
                if (DateTime.UtcNow - _launchStartedUtc < TimeSpan.FromSeconds(12))
                {
                    ShowOffline(
                        Lang.T("Launching"),
                        Lang.T("StartingHint"),
                        "\uE895",
                        false, false, false, false);
                    return;
                }

                _launching = false;
            }

            if (_failures < 2) return;

            UpdateOfflineForCurrentState();
        }

        private void UpdateOfflineForCurrentState()
        {
            var path = VlcController.FindVlc();
            var running = VlcController.IsRunning();

            if (path == null)
            {
                ShowOffline(
                    Lang.T("VlcNotFound"),
                    Lang.T("VlcNotFoundText"),
                    "\uE7BA",
                    false, false, true, true);
                return;
            }

            if (running)
            {
                ShowOffline(
                    Lang.T("VlcNoHttp"),
                    Lang.T("VlcNoHttpText"),
                    "\uE895",
                    false, true, false, true);
                return;
            }

            ShowOffline(
                Lang.T("OfflineTitle"),
                Lang.T("OfflineText"),
                "\uE7BA",
                true, false, false, true);
        }

        private void ShowOffline(
            string title,
            string text,
            string icon,
            bool showLaunch,
            bool showRestart,
            bool showDownload,
            bool showRetry)
        {
            MainPanel.Visibility = Visibility.Collapsed;
            OfflineCard.Visibility = Visibility.Visible;

            OfflineTitle.Text = title;
            OfflineText.Text = text;
            OfflineIcon.Text = icon;

            LaunchButton.Visibility = showLaunch ? Visibility.Visible : Visibility.Collapsed;
            RestartButton.Visibility = showRestart ? Visibility.Visible : Visibility.Collapsed;
            DownloadButton.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
            RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task StartVlcAsync(string path, bool restart)
        {
            _launching = true;
            _launchStartedUtc = DateTime.UtcNow;
            _failures = 0;

            ShowOffline(
                restart ? Lang.T("Restarting") : Lang.T("Launching"),
                Lang.T("StartingHint"),
                "\uE895",
                false, false, false, false);

            try
            {
                if (restart)
                {
                    await VlcController.RestartAsync(path);
                }
                else
                {
                    VlcController.Launch(path);
                }
            }
            catch (Exception ex)
            {
                _launching = false;
                ShowOffline(
                    Lang.T("LaunchFailed"),
                    Lang.T("LaunchFailedText") + ex.Message,
                    "\uE7BA",
                    !VlcController.IsRunning(), VlcController.IsRunning(), false, true);
            }
        }

        private async Task RefreshStatusOnceAsync()
        {
            var cts = _cts;
            if (cts == null) return;

            try
            {
                var status = await _client.GetStatusAsync(cts.Token);
                ApplyStatus(status);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _failures = Math.Max(_failures, 2);
                _connected = false;
                _launching = false;
                Dot.Fill = RedBrush;
                ConnText.Text = Lang.T("NoConnection");
                UpdateOfflineForCurrentState();
            }
        }

        private async Task RefreshPlaylistAsync()
        {
            var cts = _cts;
            if (cts == null) return;

            try
            {
                var items = await _client.GetPlaylistAsync(cts.Token);
                ApplyPlaylist(items);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private async Task ExecAsync(Func<CancellationToken, Task> action, bool refreshPlaylist = false)
        {
            var cts = _cts;
            if (cts == null) return;

            try
            {
                await action(cts.Token);
                await Task.Delay(150, cts.Token);
                await RefreshStatusOnceAsync();

                if (refreshPlaylist || _playlistVisible)
                {
                    await RefreshPlaylistAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _failures = Math.Max(_failures, 2);
                _connected = false;
                _launching = false;
                Dot.Fill = RedBrush;
                ConnText.Text = Lang.T("NoConnection");
                UpdateOfflineForCurrentState();
            }
        }

        private void UpdateRateButtons(double currentRate)
        {
            foreach (var button in _rateButtons)
            {
                var rate = (double)button.Tag;
                var active = Math.Abs(rate - currentRate) < 0.06;
                button.Background = active ? AccentBrush : ChipBrush;
                button.Foreground = active ? (Brush)new SolidColorBrush(Color.FromRgb(20, 20, 20)) : (Brush)new SolidColorBrush(Color.FromArgb(217, 255, 255, 255));
            }
        }

        private void UpdatePinVisual()
        {
            PinIcon.Foreground = Topmost ? AccentBrush : MutedBrush;
        }

        private void UpdateLanguageButtons()
        {
            LangRuButton.Foreground = Lang.Current == UiLanguage.Ru ? AccentBrush : MutedBrush;
            LangEnButton.Foreground = Lang.Current == UiLanguage.En ? AccentBrush : MutedBrush;
        }

        private void LangRu_Click(object sender, RoutedEventArgs e) => ApplyLanguage(UiLanguage.Ru);

        private void LangEn_Click(object sender, RoutedEventArgs e) => ApplyLanguage(UiLanguage.En);

        private void ApplyLanguage(UiLanguage language)
        {
            Lang.Apply(language);
            Lang.Save(language);

            if (_status == null)
            {
                ConnText.Text = Lang.T("Connecting");
                TitleText.Text = Lang.T("NothingPlaying");
            }

            UpdateLanguageButtons();
        }

        private static string BuildSubtitle(VlcStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status.Artist))
            {
                return string.IsNullOrWhiteSpace(status.Album)
                    ? status.Artist
                    : status.Artist + " · " + status.Album;
            }

            if (!string.IsNullOrWhiteSpace(status.Filename) &&
                !string.Equals(status.Filename, status.Title, StringComparison.OrdinalIgnoreCase))
            {
                return status.Filename;
            }

            return string.Empty;
        }

        private static int VolumePercent(int volume) =>
            (int)Math.Round(Math.Max(0, Math.Min(200, volume / 2.56)));

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0)
            {
                seconds = 0;
            }

            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;

            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            UpdatePinVisual();
            _settings.Topmost = Topmost;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_status != null && _status.State != "playing" && _status.State != "paused" && _status.PlaylistId > 0)
            {
                var id = _status.PlaylistId;
                await ExecAsync(token => _client.PlayItemAsync(id, token));
                return;
            }

            await ExecAsync(token => _client.TogglePauseAsync(token));
        }

        private async void Previous_Click(object sender, RoutedEventArgs e) =>
            await ExecAsync(token => _client.PreviousAsync(token), refreshPlaylist: true);

        private async void Next_Click(object sender, RoutedEventArgs e) =>
            await ExecAsync(token => _client.NextAsync(token), refreshPlaylist: true);

        private async void Back10_Click(object sender, RoutedEventArgs e) =>
            await ExecAsync(token => _client.SeekRelativeAsync(-10, token));

        private async void Forward10_Click(object sender, RoutedEventArgs e) =>
            await ExecAsync(token => _client.SeekRelativeAsync(10, token));

        private void Seek_PointerDown(object sender, MouseButtonEventArgs e) => _seekHeld = true;

        private void Seek_PointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!_seekHeld) return;
            _seekHeld = false;
            CommitSeek();
        }

        private void Seek_DragStarted(object sender, DragStartedEventArgs e) => _seekHeld = true;

        private void Seek_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _seekHeld = false;
            CommitSeek();
        }

        private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSeek || TimeNow == null) return;

            TimeNow.Text = FormatTime(e.NewValue);
            if (!_seekHeld)
            {
                CommitSeek();
            }
        }

        private async void CommitSeek()
        {
            if (_status == null || !_status.HasMedia || SeekSlider.Maximum <= 1) return;

            var percent = Math.Max(0, Math.Min(100, SeekSlider.Value / SeekSlider.Maximum * 100));
            if (Math.Abs(percent - _lastSeekPercent) < 0.01 &&
                DateTime.UtcNow - _lastSeekCommitUtc < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            _lastSeekPercent = percent;
            _lastSeekCommitUtc = DateTime.UtcNow;
            _lastUserSeekUtc = DateTime.UtcNow;
            await ExecAsync(token => _client.SeekPercentAsync(percent, token));
        }

        private void Volume_PointerDown(object sender, MouseButtonEventArgs e) => _volumeHeld = true;

        private void Volume_PointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!_volumeHeld) return;
            _volumeHeld = false;
            CommitVolume();
        }

        private void Volume_DragStarted(object sender, DragStartedEventArgs e) => _volumeHeld = true;

        private void Volume_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _volumeHeld = false;
            CommitVolume();
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingVolume || VolumeText == null) return;

            VolumeText.Text = (int)Math.Round(e.NewValue) + "%";
            if (!_volumeHeld)
            {
                CommitVolume();
            }
        }

        private async void CommitVolume()
        {
            var percent = Math.Max(0, Math.Min(100, VolumeSlider.Value));
            if (Math.Abs(percent - _lastVolumePercent) < 0.01 &&
                DateTime.UtcNow - _lastVolumeCommitUtc < TimeSpan.FromMilliseconds(300))
            {
                return;
            }

            _lastVolumePercent = percent;
            _lastVolumeCommitUtc = DateTime.UtcNow;
            _lastUserVolumeUtc = DateTime.UtcNow;

            var volume = (int)Math.Round(percent / 100 * 256);
            await ExecAsync(token => _client.SetVolumeAsync(volume, token));
        }

        private async void Mute_Click(object sender, RoutedEventArgs e)
        {
            var current = _status?.Volume ?? 0;
            var target = current > 0 ? 0 : Math.Max(64, _lastNonZeroVolume);
            if (target > 0)
            {
                _lastNonZeroVolume = target;
            }

            _updatingVolume = true;
            VolumeSlider.Value = Math.Min(100, target / 2.56);
            _updatingVolume = false;
            VolumeText.Text = VolumePercent(target) + "%";
            _lastUserVolumeUtc = DateTime.UtcNow;

            await ExecAsync(token => _client.SetVolumeAsync(target, token));
        }

        private async void Rate_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is double rate)) return;

            UpdateRateButtons(rate);
            await ExecAsync(token => _client.SetRateAsync(rate, token));
        }

        private async void PlaylistToggle_Click(object sender, RoutedEventArgs e)
        {
            _playlistVisible = !_playlistVisible;
            PlaylistPanel.Visibility = _playlistVisible ? Visibility.Visible : Visibility.Collapsed;
            Chevron.Text = _playlistVisible ? "\uE70E" : "\uE70D";

            if (_playlistVisible)
            {
                await RefreshPlaylistAsync();
            }
        }

        private async void Playlist_Click(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (!(source is ListBoxItem item) || !(item.DataContext is VlcPlaylistItem playlistItem))
            {
                return;
            }

            e.Handled = true;
            await ExecAsync(token => _client.PlayItemAsync(playlistItem.Id, token), refreshPlaylist: true);
        }

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            var path = VlcController.FindVlc();
            if (path == null)
            {
                UpdateOfflineForCurrentState();
                return;
            }

            await StartVlcAsync(path, restart: false);
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (VlcController.IsRunning())
            {
                ConfirmOverlay.Visibility = Visibility.Visible;
                return;
            }

            var path = VlcController.FindVlc();
            if (path != null)
            {
                _ = StartVlcAsync(path, restart: false);
            }
        }

        private void ConfirmRestart_Click(object sender, RoutedEventArgs e)
        {
            ConfirmOverlay.Visibility = Visibility.Collapsed;

            var path = VlcController.FindVlc();
            if (path != null)
            {
                _ = StartVlcAsync(path, restart: true);
            }
        }

        private void CancelRestart_Click(object sender, RoutedEventArgs e)
        {
            ConfirmOverlay.Visibility = Visibility.Collapsed;
        }

        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            Dot.Fill = MutedBrush;
            ConnText.Text = Lang.T("Checking");
            await RefreshStatusOnceAsync();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.videolan.org/vlc/",
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            HostBox.Text = _settings.Host;
            PortBox.Text = _settings.Port.ToString(CultureInfo.InvariantCulture);
            PasswordBox.Password = _settings.Password;
            LaunchOnStartBox.IsChecked = _settings.LaunchVlcOnStart;
            SettingsStatus.Text = string.Empty;
            SettingsOverlay.Visibility = Visibility.Visible;
            ConfirmOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port < 1 || port > 65535)
            {
                SettingsStatus.Text = Lang.T("PortInvalid");
                return;
            }

            var host = (HostBox.Text ?? string.Empty).Trim();
            _settings.Host = host.Length == 0 ? "127.0.0.1" : host;
            _settings.Port = port;
            _settings.Password = PasswordBox.Password ?? string.Empty;
            _settings.LaunchVlcOnStart = LaunchOnStartBox.IsChecked == true;
            _settings.Save();

            _client.Configure(_settings.Host, _settings.Port, _settings.Password);
            SettingsOverlay.Visibility = Visibility.Collapsed;
            StartPolling();
        }

        private async void CheckConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port < 1 || port > 65535)
            {
                SettingsStatus.Text = Lang.T("PortInvalid");
                return;
            }

            var host = (HostBox.Text ?? string.Empty).Trim();
            if (host.Length == 0) host = "127.0.0.1";

            SettingsStatus.Text = Lang.T("Checking");

            using (var client = new VlcClient())
            {
                client.Configure(host, port, PasswordBox.Password ?? string.Empty);
                try
                {
                    var status = await client.GetStatusAsync(CancellationToken.None);
                    SettingsStatus.Text = string.IsNullOrWhiteSpace(status.DisplayTitle)
                        ? Lang.T("ConnectionOk")
                        : Lang.T("ConnectionOkTitle") + status.DisplayTitle;
                }
                catch (Exception ex)
                {
                    SettingsStatus.Text = Lang.T("NoConnectionPrefix") + ex.Message;
                }
            }
        }
    }
}
