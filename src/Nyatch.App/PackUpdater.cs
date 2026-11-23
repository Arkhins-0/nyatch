using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nyatch.Core;

namespace Nyatch.App;

/// <summary>
/// Keeps installed character packs up to date: a while after Nyatch starts, then every few hours, the
/// pack list is fetched and every pack on this PC with a newer version listed is downloaded and swapped
/// in (each is only swapped once its whole file has arrived and passed the list's checks). Packs that
/// are not installed are never fetched. Off with Characters.AutoUpdate.
/// </summary>
internal sealed class PackUpdater
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2), Interval = TimeSpan.FromHours(6);

    private readonly SettingsStore _settings;
    private readonly Action _changed;
    private readonly DispatcherTimer _timer = new() { Interval = FirstCheck };
    private bool _busy;

    /// <param name="changed">Called after a pack was updated, so the notches load its pictures again.</param>
    public PackUpdater(SettingsStore settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            await CheckAsync();
        };
    }

    public void Start() => _timer.Start();

    public async Task CheckAsync()
    {
        if (_busy || _settings.Get("Characters.AutoUpdate") != "true") return;
        var packs = new CharacterPacks();
        var installed = packs.Installed();
        if (installed.Count == 0) return;
        _busy = true;
        try
        {
            var listed = await packs.AvailableAsync();
            var updated = false;
            foreach (var mine in installed)
            {
                if (listed.FirstOrDefault(p => p.Id == mine.Id) is not { } newer || !IsNewer(newer.Version, mine.Version)) continue;
                try
                {
                    await packs.DownloadAsync(newer);
                    updated = true;
                }
                catch (Exception error) when (error is System.IO.IOException or System.Net.Http.HttpRequestException or System.IO.InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
                {
                    // Tried again at the next check; the installed version keeps working meanwhile.
                }
            }
            if (updated) _changed();
        }
        catch (Exception error) when (error is System.IO.IOException or System.Net.Http.HttpRequestException or System.IO.InvalidDataException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // No connection, or no list yet: tried again later.
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Whether one version is newer than another ("1.10" is newer than "1.9").</summary>
    public static bool IsNewer(string version, string than) =>
        Version.TryParse(version, out var a) && Version.TryParse(than, out var b)
            ? a > b
            : !string.Equals(version, than, StringComparison.Ordinal) && string.CompareOrdinal(version, than) > 0;
}
