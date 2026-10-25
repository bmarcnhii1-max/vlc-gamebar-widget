using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace VlcGameBarWidget
{
    public sealed class VlcStatus
    {
        public string State { get; set; } = "stopped";
        public double Time { get; set; }
        public double Length { get; set; }
        public double Rate { get; set; } = 1;
        public int Volume { get; set; }
        public int PlaylistId { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Filename { get; set; }

        public bool HasMedia => Length > 0.5 || !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Filename);

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return Title;
                if (!string.IsNullOrWhiteSpace(Filename)) return Filename;
                return null;
            }
        }
    }

    public sealed class VlcPlaylistItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private double _duration;
        private bool _isCurrent;

        public int Id { get; set; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public double Duration
        {
            get => _duration;
            set
            {
                if (Math.Abs(_duration - value) < 0.01) return;
                _duration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value) return;
                _isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }

        public string DurationText
        {
            get
            {
                if (_duration <= 0) return "—";
                var span = TimeSpan.FromSeconds(_duration);
                return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal sealed class VlcClient : IDisposable
    {
        private readonly HttpClient _http;
        private string _baseAddress = "http://127.0.0.1:8080/requests/";
        private string _authorization = "Basic Og==";

        public VlcClient()
        {
            _http = new HttpClient(new HttpClientHandler { UseCookies = false })
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
        }

        public void Configure(string host, int port, string password)
        {
            _baseAddress = $"http://{host}:{port}/requests/";
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + (password ?? string.Empty)));
            _authorization = "Basic " + raw;
        }

        public async Task<VlcStatus> GetStatusAsync(CancellationToken token)
        {
            var xml = await GetAsync("status.xml", token).ConfigureAwait(false);
            var doc = ParseXml(xml, "root");
            var root = doc.Root;

            var status = new VlcStatus
            {
                State = ElementValue(root, "state") ?? "stopped",
                Time = ParseDouble(ElementValue(root, "time")),
                Length = ParseDouble(ElementValue(root, "length")),
                Rate = ParseDouble(ElementValue(root, "rate"), 1),
                Volume = (int)Math.Round(ParseDouble(ElementValue(root, "volume"))),
                PlaylistId = (int)ParseDouble(ElementValue(root, "currentplid")),
            };

            var meta = root.Elements()
                .Where(e => e.Name.LocalName == "information")
                .SelectMany(e => e.Elements())
                .FirstOrDefault(e => e.Name.LocalName == "category" && (string)e.Attribute("name") == "meta");

            var infos = meta?.Elements().Where(e => e.Name.LocalName == "info").ToList() ?? new List<XElement>();
            status.Title = InfoValue(infos, "title");
            status.Artist = InfoValue(infos, "artist");
            status.Album = InfoValue(infos, "album");
            status.Filename = InfoValue(infos, "filename");

            return status;
        }

        public async Task<List<VlcPlaylistItem>> GetPlaylistAsync(CancellationToken token)
        {
            var xml = await GetAsync("playlist.xml", token).ConfigureAwait(false);
            var doc = ParseXml(xml, "node");

            return doc.Descendants()
                .Where(e => e.Name.LocalName == "leaf")
                .Select(leaf => new VlcPlaylistItem
                {
                    Id = (int)ParseDouble((string)leaf.Attribute("id")),
                    Name = (string)leaf.Attribute("name") ?? string.Empty,
                    Duration = ParseDouble((string)leaf.Attribute("duration")),
                    IsCurrent = (string)leaf.Attribute("current") == "current",
                })
                .Where(item => item.Id > 0)
                .ToList();
        }

        public Task TogglePauseAsync(CancellationToken token) => SendAsync("command=pl_pause", token);

        public Task NextAsync(CancellationToken token) => SendAsync("command=pl_next", token);

        public Task PreviousAsync(CancellationToken token) => SendAsync("command=pl_previous", token);

        public Task PlayItemAsync(int id, CancellationToken token) =>
            SendAsync("command=pl_play&id=" + id.ToString(CultureInfo.InvariantCulture), token);

        public Task SeekRelativeAsync(int seconds, CancellationToken token) =>
            SendAsync(
                "command=seek&val=" + (seconds > 0 ? "%2B" : "-") + Math.Abs(seconds).ToString(CultureInfo.InvariantCulture) + "S",
                token);

        public Task SeekPercentAsync(double percent, CancellationToken token) =>
            SendAsync(
                "command=seek&val=" + percent.ToString("0.#", CultureInfo.InvariantCulture) + "%25",
                token);

        public Task SetRateAsync(double rate, CancellationToken token) =>
            SendAsync("command=rate&val=" + rate.ToString("0.##", CultureInfo.InvariantCulture), token);

        public Task SetVolumeAsync(int volume, CancellationToken token) =>
            SendAsync("command=volume&val=" + Math.Max(0, volume).ToString(CultureInfo.InvariantCulture), token);

        private async Task SendAsync(string query, CancellationToken token)
        {
            await GetAsync("status.xml?" + query, token).ConfigureAwait(false);
        }

        private async Task<string> GetAsync(string relative, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress + relative)))
            {
                request.Headers.TryAddWithoutValidation("Authorization", _authorization);
                using (var response = await _http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("VLC HTTP " + (int)response.StatusCode);
                    }

                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static XDocument ParseXml(string xml, string expectedRoot)
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root == null || doc.Root.Name.LocalName != expectedRoot)
            {
                throw new InvalidOperationException("Unexpected response from VLC");
            }

            return doc;
        }

        private static string ElementValue(XElement root, string name) =>
            root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        private static string InfoValue(IEnumerable<XElement> infos, string name) =>
            infos.FirstOrDefault(e => (string)e.Attribute("name") == name)?.Value;

        private static double ParseDouble(string value, double fallback = 0)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }

        public void Dispose() => _http.Dispose();
    }
}
