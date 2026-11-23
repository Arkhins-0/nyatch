using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Nyatch.Core;

namespace Nyatch.App;

// The Online page: the connection (ping, and a download speed test) and your links, each showing
// whether its site is up. Only reached while the page is on show: ping to Cloudflare's 1.1.1.1, the
// speed test from speed.cloudflare.com, and each link's own site. The island draws it (native).
internal sealed partial class DeckWindow
{
    // The island's text box on this page.
    private const int LinkBox = 30;

    private static readonly HttpClient Web = CreateWeb();

    private static HttpClient CreateWeb()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyatch/1.0");
        return client;
    }

    // What the page shows.
    private string _ping = "–", _speed = "–", _speedNote = "";
    private bool _pingQuiet;
    private bool _pinging;
    private CancellationTokenSource? _speedTest;
    private bool _addingLink;
    private string _linkDraft = "", _linkProblem = "";
    private int _onlineTick;
    // Each link's site: 0 checking, 1 up, 2 slow or refusing, 3 down; and what to say.
    private readonly Dictionary<string, (int Dot, string State)> _sites = [];
    private readonly List<Action> _onlineActions = [];

    private bool OnlineShown => _expanded && _tab >= 0 && _tab < _tabs.Count && _tabs[_tab].Native == "online";

    private void OnlineChanged()
    {
        if (OnlineShown) SendNativeData();
    }

    /// <summary>The page's every-second work: a ping every six seconds.</summary>
    private void OnlineEverySecond()
    {
        if (_onlineTick++ % 6 == 0) _ = Ping();
    }

    /// <summary>On show: ping now, and check the sites again.</summary>
    private void OnlineOnShow()
    {
        _onlineTick = 0;
        foreach (var link in _deck.Links)
        {
            if (Uri.TryCreate(link.Url, UriKind.Absolute, out var address)) _ = CheckSite(link.Id, address);
        }
        OnlineChanged();
    }

    /// <summary>The Online page for the island (see its DrawDeckOnline).</summary>
    private string OnlineData()
    {
        _onlineActions.Clear();
        int Act(Action action)
        {
            _onlineActions.Add(action);
            return _onlineActions.Count - 1;
        }
        static string Clean(string text) => text.Replace('\u001E', ' ').Replace('\u001F', ' ');
        var records = new List<string>();
        var test = Act(() => _ = SpeedTest());
        records.Add(string.Join('\u001F', "C", _ping, _pingQuiet ? "1" : "0", _speed, _speedNote, _speedTest is null ? "Speed test" : "Stop", test.ToString()));
        var add = Act(() =>
        {
            _addingLink = true;
            _linkDraft = "";
            _linkProblem = "";
            OnlineChanged();
            Island.DeckFocusField(LinkBox);
        });
        records.Add($"H\u001F{add}");
        if (_addingLink) records.Add(string.Join('\u001F', "W", Clean(_linkDraft), Clean(_linkProblem)));
        if (_deck.Links.Count == 0 && !_addingLink) records.Add("K\u001FAdd sites you open often. Each shows whether it is up.");
        foreach (var link in _deck.Links)
        {
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var address)) continue;
            var (dot, state) = _sites.TryGetValue(link.Id, out var site) ? site : (0, "…");
            var open = Act(() =>
            {
                OpenAddress(address);
                Collapse();
            });
            var remove = Act(() =>
            {
                _deck.Links.RemoveAll(l => l.Id == link.Id);
                _deck.Save();
                OnlineChanged();
            });
            // The site's own icon, once it has been fetched.
            var icon = WebFile(new Uri($"{address.Scheme}://{address.Host}/favicon.ico")) ?? "";
            records.Add(string.Join('\u001F', "L", Clean(link.Name), state, dot.ToString(), open.ToString(), remove.ToString(), Clean(icon)));
        }
        return string.Join('\u001E', records);
    }

    /// <summary>A click on the Online page, from the island.</summary>
    private void OnlineAction(int action)
    {
        if (action >= 0 && action < _onlineActions.Count) _onlineActions[action]();
    }

    /// <summary>The link box, from the island: 0 its text changed, 1 Enter, 2 Esc, 7 typing ended.</summary>
    private void OnlineEdit(int box, int what)
    {
        if (box != LinkBox) return;
        switch (what)
        {
            case 0:
                _linkDraft = Island.DeckEditText();
                if (_linkProblem.Length > 0)
                {
                    _linkProblem = "";
                    OnlineChanged();
                }
                break;
            case 1:
                if (WebLink.From(Island.DeckEditText(submitted: true)) is not { } link)
                {
                    _linkProblem = "That is not a web address. Try something like github.com.";
                    OnlineChanged();
                    break;
                }
                _deck.Links.Add(link);
                _deck.Save();
                _addingLink = false;
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var address)) _ = CheckSite(link.Id, address);
                OnlineChanged();
                break;
            case 2 or 7:
                if (!_addingLink) break;
                _addingLink = false;
                OnlineChanged();
                break;
        }
    }

    private async Task Ping()
    {
        if (_pinging) return;
        _pinging = true;
        try
        {
            using var pinger = new Ping();
            var host = _settings.Get("Deck.PingHost").Trim();
            var reply = await pinger.SendPingAsync(host.Length > 0 ? host : "1.1.1.1", 2000);
            _ping = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "No reply";
            _pingQuiet = reply.Status != IPStatus.Success || reply.RoundtripTime >= 80;
        }
        catch (Exception e) when (e is PingException or InvalidOperationException or ArgumentException or System.Net.Sockets.SocketException)
        {
            _ping = "Offline";
            _pingQuiet = true;
        }
        finally
        {
            _pinging = false;
        }
        OnlineChanged();
    }

    private async Task SpeedTest()
    {
        if (_speedTest is not null)
        {
            _speedTest.Cancel();
            return;
        }
        var testing = new CancellationTokenSource();
        _speedTest = testing;
        _speed = "…";
        _speedNote = "Measuring";
        OnlineChanged();
        try
        {
            const int Bytes = 25_000_000;
            using var reply = await Web.GetAsync($"https://speed.cloudflare.com/__down?bytes={Bytes}", HttpCompletionOption.ResponseHeadersRead, testing.Token);
            reply.EnsureSuccessStatusCode();
            await using var stream = await reply.Content.ReadAsStreamAsync(testing.Token);
            var buffer = new byte[81920];
            long got = 0;
            var clock = Stopwatch.StartNew();
            var shown = TimeSpan.Zero;
            int read;
            // At most 10 seconds: enough for a steady figure on a slow line too.
            while ((read = await stream.ReadAsync(buffer, testing.Token)) > 0 && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                got += read;
                if (clock.Elapsed - shown > TimeSpan.FromMilliseconds(250))
                {
                    shown = clock.Elapsed;
                    _speed = Mbps(got, clock.Elapsed);
                    OnlineChanged();
                }
            }
            _speed = Mbps(got, clock.Elapsed);
            _speedNote = $"{got / 1_000_000.0:0.#} MB in {clock.Elapsed.TotalSeconds:0.#} s";
        }
        catch (OperationCanceledException)
        {
            _speedNote = "Stopped";
        }
        catch (HttpRequestException)
        {
            _speed = "–";
            _speedNote = "Could not reach Cloudflare";
        }
        finally
        {
            testing.Dispose();
            _speedTest = null;
            OnlineChanged();
        }
    }

    private static string Mbps(long bytes, TimeSpan time) =>
        time.TotalSeconds <= 0 ? "…" : $"{bytes * 8 / time.TotalSeconds / 1_000_000:0.#} Mbps";

    /// <summary>Is the site up: green when it answers, amber when slow or it refuses, red when it does not answer.</summary>
    private async Task CheckSite(string id, Uri address)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(HttpMethod.Head, address);
            using var reply = await Web.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            var took = clock.ElapsedMilliseconds;
            var ok = (int)reply.StatusCode < 400 || reply.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
            _sites[id] = (ok && took < 1500 ? 1 : 2, ok ? $"{took} ms" : $"{(int)reply.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            _sites[id] = (3, "Down");
        }
        OnlineChanged();
    }
}
