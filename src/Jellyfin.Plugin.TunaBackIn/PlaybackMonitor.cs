using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TunaBackIn.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TunaBackIn;

/// <summary>
/// Feeds session playback events into the <see cref="ResumeTracker"/>.
/// </summary>
public sealed class PlaybackMonitor : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ResumeTracker _tracker;
    private readonly ResumeService _resumeService;
    private readonly ILogger<PlaybackMonitor> _logger;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackMonitor"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="tracker">The tracker.</param>
    /// <param name="resumeService">The resume service.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackMonitor(ISessionManager sessionManager, ResumeTracker tracker, ResumeService resumeService, ILogger<PlaybackMonitor> logger)
    {
        _sessionManager = sessionManager;
        _tracker = tracker;
        _resumeService = resumeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        await _stopping.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private static bool IsMusic(BaseItem? item) => item is Audio and not AudioBook;

    private static bool IsVideo(BaseItem? item) => item is not null && item.MediaType == MediaType.Video;

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => OnPlaybackEvent(e, isStart: true);

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => OnPlaybackEvent(e, isStart: false);

    private void OnPlaybackEvent(PlaybackProgressEventArgs e, bool isStart)
    {
        try
        {
            var session = e.Session;
            if (session is null || string.IsNullOrEmpty(session.DeviceId) || session.UserId.Equals(Guid.Empty))
            {
                return;
            }

            if (IsMusic(e.Item))
            {
                var queue = session.NowPlayingQueue?.Select(q => new QueueItemRef(q.Id, q.PlaylistItemId)).ToList();
                _tracker.AudioActivity(
                    session.UserId,
                    session.DeviceId,
                    queue,
                    e.Item!.Id,
                    session.PlaylistItemId,
                    e.PlaybackPositionTicks ?? session.PlayState?.PositionTicks ?? 0,
                    DateTimeOffset.UtcNow);
            }
            else if (isStart && IsVideo(e.Item))
            {
                _tracker.NonAudioStarted(session.UserId, session.DeviceId, DateTimeOffset.UtcNow);
            }
        }
#pragma warning disable CA1031 // An exception in an event handler must not break playback reporting.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "TunaBackIn failed to handle a playback event");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            var session = e.Session;
            if (session is null || string.IsNullOrEmpty(session.DeviceId) || session.UserId.Equals(Guid.Empty) || e.Item is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (IsMusic(e.Item))
            {
                _tracker.AudioStopped(session.UserId, session.DeviceId, e.Item.Id, e.PlaybackPositionTicks, now);
            }
            else if (IsVideo(e.Item))
            {
                var offered = _tracker.NonAudioStopped(session.UserId, session.DeviceId, now);
                if (offered is not null)
                {
                    _resumeService.FillTitle(offered);
                    _logger.LogInformation("Offering to resume \"{Title}\" on {Device}", offered.Title, session.DeviceName);
                    if (Plugin.Instance?.Configuration.AutoResume == true)
                    {
                        _ = AutoResumeAsync(session.UserId, session.DeviceId);
                    }
                }
            }
        }
#pragma warning disable CA1031 // An exception in an event handler must not break playback reporting.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "TunaBackIn failed to handle a playback stop");
        }
    }

    private async Task AutoResumeAsync(Guid userId, string deviceId)
    {
        try
        {
            // Wait out the settle delay so autoplay of the next episode wins over the music.
            await Task.Delay(TrackerTiming.Default.SettleDelay + TimeSpan.FromSeconds(1), _stopping.Token).ConfigureAwait(false);
            if (_tracker.GetOffer(userId, deviceId, _resumeService.IsPlaying(userId, deviceId), DateTimeOffset.UtcNow) is not null)
            {
                await _resumeService.ResumeAsync(userId, deviceId, _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down.
        }
#pragma warning disable CA1031 // Fire-and-forget task: log, never throw.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "TunaBackIn auto-resume failed");
        }
    }
}
