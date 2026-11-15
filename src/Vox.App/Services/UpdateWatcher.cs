using System.Net.Http;
using Vox.Core;
using Vox.Core.Updates;

namespace Vox.App.Services;

/// <summary>
/// Asks GitHub once a day whether a newer Vox is out (docs/adr/038) and remembers the answer for the tray and
/// the settings. It only learns a version number: nothing is downloaded or installed. Lives on the UI thread;
/// the request itself runs on the thread pool, so no network ever holds the UI or the start.
/// </summary>
internal sealed class UpdateWatcher : IDisposable
{
    private readonly Settings _settings;
    private readonly UpdateChecker _checker;
    private readonly CancellationTokenSource _closing = new();
    private readonly CancellationToken _closed; // stays readable after Dispose
    private CancellationTokenSource? _daily;

    public UpdateWatcher(Settings settings, HttpClient http, Version current)
    {
        _settings = settings;
        _checker = new UpdateChecker(http, current);
        _closed = _closing.Token;
        CurrentVersion = current.ToString(3);
    }

    /// <summary>This build, three parts ("2.0.0").</summary>
    public string CurrentVersion { get; }

    /// <summary>The last answer in this run; null before the first check.</summary>
    public UpdateCheck? Last { get; private set; }

    /// <summary>A check finished, the daily one or the user's (UI thread).</summary>
    public event Action<UpdateCheck>? Checked;

    /// <summary>The daily check found a version the user was not told about yet (UI thread).</summary>
    public event Action<UpdateCheck>? Announce;

    /// <summary>Starts or stops the daily check to match the «Проверять обновления» setting.</summary>
    public void Apply()
    {
        _daily?.Cancel();
        _daily?.Dispose();
        _daily = null;
        if (_closed.IsCancellationRequested)
        {
            return;
        }

        if (!_settings.CheckUpdates)
        {
            Log.Info("[UPDATE] Checks are off in the settings");
            return;
        }

        _daily = CancellationTokenSource.CreateLinkedTokenSource(_closed);
        _ = RunDailyAsync(_daily.Token);
    }

    /// <summary>The «Проверить» button: asks now, whatever the setting and the schedule say.</summary>
    public Task<UpdateCheck> CheckNowAsync() => CheckAsync(_closed);

    public void Dispose()
    {
        _closing.Cancel(); // nothing is written to config.json after this: the data may be being deleted
        _daily?.Dispose();
        _daily = null;
        _closing.Dispose();
    }

    private async Task RunDailyAsync(CancellationToken stop)
    {
        try
        {
            var wait = UpdateSchedule.UntilNext(_settings.UpdateChecked, DateTimeOffset.Now);
            while (true)
            {
                await Task.Delay(wait, stop);
                var check = await CheckAsync(stop);
                if (UpdateSchedule.ShouldNotify(check, _settings.UpdateNotified))
                {
                    Announce?.Invoke(check);
                    MarkAnnounced(check);
                }

                wait = check.Status == UpdateStatus.Unknown ? UpdateSchedule.RetryDelay : UpdateSchedule.Interval;
            }
        }
        catch (OperationCanceledException)
        {
            // Vox is closing or the setting was switched off.
        }
        catch (Exception e)
        {
            Log.Error("[UPDATE] The daily check stopped", e); // never the user's problem
        }
    }

    private async Task<UpdateCheck> CheckAsync(CancellationToken stop)
    {
        var check = await Task.Run(() => _checker.CheckAsync(stop), stop); // back on the UI thread after this
        stop.ThrowIfCancellationRequested();
        Last = check;
        Log.Info(check.Status switch
        {
            UpdateStatus.Available => $"[UPDATE] Vox {check.Latest?.ToString(3)} is out, this is {CurrentVersion}",
            UpdateStatus.UpToDate => $"[UPDATE] Up to date ({CurrentVersion}, latest release {check.Latest?.ToString(3)})",
            _ => $"[UPDATE] No answer: {check.Detail}",
        });
        if (check.Status != UpdateStatus.Unknown)
        {
            _settings.UpdateChecked = DateTimeOffset.Now;
            Save();
        }

        Checked?.Invoke(check);
        return check;
    }

    /// <summary>The user has seen this version (tray notice or the settings card): no tray notice about it again.</summary>
    public void MarkAnnounced(UpdateCheck check)
    {
        if (UpdateSchedule.ShouldNotify(check, _settings.UpdateNotified))
        {
            _settings.UpdateNotified = check.Latest?.ToString(3);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("[UPDATE] Could not save the settings", e);
        }
    }
}
