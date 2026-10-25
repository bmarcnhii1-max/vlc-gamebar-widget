using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace VlcGameBarWidget
{
    public sealed partial class Widget1 : Page
    {
        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 255, 138, 0));
        private static readonly SolidColorBrush ChipBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        private static readonly SolidColorBrush ChipTextBrush = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
        private static readonly SolidColorBrush ChipActiveTextBrush = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20));
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Color.FromArgb(255, 229, 72, 77));

        private readonly VlcClient _client = new VlcClient();
        private readonly ObservableCollection<VlcPlaylistItem> _playlist = new ObservableCollection<VlcPlaylistItem>();

        private CancellationTokenSource _cts;
        private VlcStatus _status;
        private string _playlistSignature = string.Empty;
        private string _host = "127.0.0.1";
        private int _port = 8080;
        private string _password = "gamebar";
        private int _lastNonZeroVolume = 256;
        private int _failures;
        private bool _loaded;
        private bool _updatingSeek;
        private bool _updatingVolume;
        private bool _seekHeld;
        private bool _volumeHeld;
        private volatile bool _playlistVisible;
        private DateTime _lastUserSeekUtc = DateTime.MinValue;
        private DateTime _lastUserVolumeUtc = DateTime.MinValue;

        public Widget1()
        {
            InitializeComponent();
            PlaylistList.ItemsSource = _playlist;
            UpdateRateButtons(1);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            LoadSettings();
            StartPolling();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            _cts?.Cancel();
            _cts = null;
        }

        private void LoadSettings()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue("Host", out var host) && host is string hostText && !string.IsNullOrWhiteSpace(hostText))
            {
                _host = hostText;
            }

            if (values.TryGetValue("Port", out var port) && port is int portNumber && portNumber > 0)
            {
                _port = portNumber;
            }

            if (values.TryGetValue("Password", out var password) && password is string passwordText)
            {
                _password = passwordText;
            }

            _client.Configure(_host, _port, _password);
        }

        private void StartPolling()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = PollLoopAsync(_cts.Token);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            var tick = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var status = await _client.GetStatusAsync(token).ConfigureAwait(false);
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ApplyStatus(status));

                    if (_playlistVisible && tick % 3 == 0)
                    {
                        var items = await _client.GetPlaylistAsync(token).ConfigureAwait(false);
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ApplyPlaylist(items));
                    }

                    tick++;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, HandlePollFailure);
                }

                try
                {
                    await Task.Delay(900, token).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }

        private void ApplyStatus(VlcStatus status)
        {
            _status = status;
            _failures = 0;
            OfflinePanel.Visibility = Visibility.Collapsed;
            Dot.Fill = GreenBrush;
            ConnText.Text = status.State == "playing" ? "Играет" : status.State == "paused" ? "Пауза" : "Стоп";

            TitleText.Text = string.IsNullOrWhiteSpace(status.DisplayTitle)
                ? "Ничего не воспроизводится"
                : status.DisplayTitle;

            var subtitle = BuildSubtitle(status);
            SubText.Text = subtitle;
            SubText.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;

            PlayIcon.Glyph = status.State == "playing" ? "\uE769" : "\uE768";

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
            SeekPanel.Opacity = status.HasMedia ? 1 : 0.4;

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

            MuteIcon.Glyph = status.Volume == 0 ? "\uE74F" : "\uE767";
        }

        private void ApplyPlaylist(System.Collections.Generic.List<VlcPlaylistItem> items)
        {
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
            _failures++;
            if (_failures >= 2)
            {
                ShowOffline();
                return;
            }

            Dot.Fill = RedBrush;
            ConnText.Text = "Нет связи";
        }

        private void ShowOffline()
        {
            _failures = 2;
            _status = null;
            Dot.Fill = RedBrush;
            ConnText.Text = "Нет связи";
            TitleText.Text = "VLC не отвечает";
            SubText.Text = string.Empty;
            SubText.Visibility = Visibility.Collapsed;
            SeekSlider.IsEnabled = false;
            OfflinePanel.Visibility = Visibility.Visible;
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
                ShowOffline();
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
                ShowOffline();
            }
        }

        private void UpdateRateButtons(double currentRate)
        {
            foreach (var button in RatePanel.Children.OfType<Button>())
            {
                if (!double.TryParse(button.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                {
                    continue;
                }

                var active = Math.Abs(rate - currentRate) < 0.06;
                button.Background = active ? AccentBrush : ChipBrush;
                button.Foreground = active ? ChipActiveTextBrush : ChipTextBrush;
            }
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

        private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _seekHeld = true;

        private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_seekHeld) return;
            _seekHeld = false;
            CommitSeek();
        }

        private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_seekHeld) return;
            _seekHeld = false;
            CommitSeek();
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingSeek) return;
            TimeNow.Text = FormatTime(e.NewValue);
            if (!_seekHeld) CommitSeek();
        }

        private async void CommitSeek()
        {
            if (_status == null || !_status.HasMedia || SeekSlider.Maximum <= 1) return;

            var percent = Math.Max(0, Math.Min(100, SeekSlider.Value / SeekSlider.Maximum * 100));
            _lastUserSeekUtc = DateTime.UtcNow;
            await ExecAsync(token => _client.SeekPercentAsync(percent, token));
        }

        private void VolumeSlider_PointerPressed(object sender, PointerRoutedEventArgs e) => _volumeHeld = true;

        private void VolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_volumeHeld) return;
            _volumeHeld = false;
            CommitVolume();
        }

        private void VolumeSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_volumeHeld) return;
            _volumeHeld = false;
            CommitVolume();
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingVolume) return;
            VolumeText.Text = (int)Math.Round(e.NewValue) + "%";
            if (!_volumeHeld) CommitVolume();
        }

        private async void CommitVolume()
        {
            var percent = Math.Max(0, Math.Min(100, VolumeSlider.Value));
            var volume = (int)Math.Round(percent / 100 * 256);
            _lastUserVolumeUtc = DateTime.UtcNow;
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
            if (!(sender is Button button) ||
                !double.TryParse(button.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                return;
            }

            UpdateRateButtons(rate);
            await ExecAsync(token => _client.SetRateAsync(rate, token));
        }

        private async void PlaylistToggle_Click(object sender, RoutedEventArgs e)
        {
            _playlistVisible = !_playlistVisible;
            PlaylistPanel.Visibility = _playlistVisible ? Visibility.Visible : Visibility.Collapsed;

            if (_playlistVisible)
            {
                await RefreshPlaylistAsync();
            }
        }

        private async void Playlist_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is VlcPlaylistItem item)
            {
                await ExecAsync(token => _client.PlayItemAsync(item.Id, token), refreshPlaylist: true);
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            HostBox.Text = _host;
            PortBox.Text = _port.ToString(CultureInfo.InvariantCulture);
            PasswordBox.Password = _password;
            SettingsStatus.Text = string.Empty;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port < 1 || port > 65535)
            {
                SettingsStatus.Text = "Порт должен быть числом от 1 до 65535";
                return;
            }

            var host = (HostBox.Text ?? string.Empty).Trim();
            _host = host.Length == 0 ? "127.0.0.1" : host;
            _port = port;
            _password = PasswordBox.Password ?? string.Empty;

            var values = ApplicationData.Current.LocalSettings.Values;
            values["Host"] = _host;
            values["Port"] = _port;
            values["Password"] = _password;

            _client.Configure(_host, _port, _password);
            SettingsPanel.Visibility = Visibility.Collapsed;
            StartPolling();
        }

        private async void CheckConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port < 1 || port > 65535)
            {
                SettingsStatus.Text = "Порт должен быть числом от 1 до 65535";
                return;
            }

            var host = (HostBox.Text ?? string.Empty).Trim();
            if (host.Length == 0) host = "127.0.0.1";

            SettingsStatus.Text = "Проверяю…";

            using (var client = new VlcClient())
            {
                client.Configure(host, port, PasswordBox.Password ?? string.Empty);
                try
                {
                    var status = await client.GetStatusAsync(CancellationToken.None);
                    SettingsStatus.Text = string.IsNullOrWhiteSpace(status.DisplayTitle)
                        ? "Связь есть"
                        : "Связь есть: " + status.DisplayTitle;
                }
                catch (Exception ex)
                {
                    SettingsStatus.Text = "Нет связи: " + ex.Message;
                }
            }
        }

        private async void Reconnect_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusOnceAsync();
        }
    }
}
