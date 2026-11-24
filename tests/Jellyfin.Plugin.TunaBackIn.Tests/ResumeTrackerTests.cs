using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.TunaBackIn.Core;
using Xunit;

namespace Jellyfin.Plugin.TunaBackIn.Tests;

public class ResumeTrackerTests
{
    private const string Device = "tv";
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid[] Tracks = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private readonly MemoryStore _store = new();

    private static List<QueueItemRef> Queue() => Tracks.Select((t, i) => new QueueItemRef(t, "p" + i)).ToList();

    private ResumeTracker NewTracker() => new(_store, () => TrackerTiming.Default);

    /// <summary>Album playing, on track 2, 90 s in; video started at T0+60s.</summary>
    private static void PlayMusicThenVideo(ResumeTracker t)
    {
        t.AudioActivity(User, Device, Queue(), Tracks[0], "p0", 0, T0);
        t.AudioActivity(User, Device, null, Tracks[1], "p1", 80 * TimeSpan.TicksPerSecond, T0.AddSeconds(50));
        t.AudioStopped(User, Device, Tracks[1], 90 * TimeSpan.TicksPerSecond, T0.AddSeconds(60));
        t.NonAudioStarted(User, Device, T0.AddSeconds(61));
    }

    [Fact]
    public void FullFlow_OffersQueueAtTrackAndPosition()
    {
        var t = NewTracker();
        PlayMusicThenVideo(t);
        Assert.Equal(ResumeState.Pending, t.Get(User, Device)!.State);

        var end = T0.AddMinutes(30);
        Assert.NotNull(t.NonAudioStopped(User, Device, end));

        Assert.Null(t.GetOffer(User, Device, deviceIsPlaying: false, end.AddSeconds(1))); // settle delay
        var offer = t.GetOffer(User, Device, deviceIsPlaying: false, end.AddSeconds(5));
        Assert.NotNull(offer);
        Assert.Equal(Tracks, offer!.ItemIds);
        Assert.Equal(1, offer.CurrentIndex);
        Assert.Equal(90 * TimeSpan.TicksPerSecond, offer.PositionTicks);
    }

    [Fact]
    public void ReversedOrder_VideoStartBeforeAudioStop_StillArms()
    {
        var t = NewTracker();
        t.AudioActivity(User, Device, Queue(), Tracks[2], "p2", 10, T0);
        t.NonAudioStarted(User, Device, T0.AddSeconds(200)); // last audio tick is older than the 2 min window...
        t.AudioActivity(User, Device, null, Tracks[2], "p2", 999, T0.AddSeconds(200.5)); // ...late tick
        t.AudioStopped(User, Device, Tracks[2], 1000, T0.AddSeconds(201)); // ...but the stop follows right after

        var e = t.Get(User, Device)!;
        Assert.Equal(ResumeState.Pending, e.State);
        Assert.Equal(2, e.CurrentIndex);
        Assert.Equal(1000, e.PositionTicks);
    }

    [Fact]
    public void StaleMusic_DoesNotArm()
    {
        var t = NewTracker();
        t.AudioActivity(User, Device, Queue(), Tracks[0], "p0", 0, T0);
        t.AudioStopped(User, Device, Tracks[0], 5, T0.AddSeconds(10));
        t.NonAudioStarted(User, Device, T0.AddHours(3));
        Assert.Equal(ResumeState.Idle, t.Get(User, Device)!.State);
        Assert.Null(t.NonAudioStopped(User, Device, T0.AddHours(4)));
    }

    [Fact]
    public void AutoplayNextEpisode_GoesBackToPending()
    {
        var t = NewTracker();
        PlayMusicThenVideo(t);
        var end = T0.AddMinutes(30);
        t.NonAudioStopped(User, Device, end);
        t.NonAudioStarted(User, Device, end.AddSeconds(2));
        Assert.Equal(ResumeState.Pending, t.Get(User, Device)!.State);
        Assert.Null(t.GetOffer(User, Device, false, end.AddSeconds(10)));

        t.NonAudioStopped(User, Device, end.AddMinutes(30));
        Assert.NotNull(t.GetOffer(User, Device, false, end.AddMinutes(30).AddSeconds(5)));
    }

    [Fact]
    public void NoOffer_WhileDeviceIsPlaying_OrAfterExpiry()
    {
        var t = NewTracker();
        PlayMusicThenVideo(t);
        var end = T0.AddMinutes(30);
        t.NonAudioStopped(User, Device, end);
        Assert.Null(t.GetOffer(User, Device, deviceIsPlaying: true, end.AddSeconds(10)));
        Assert.Null(t.GetOffer(User, Device, false, end.AddHours(7)));
        Assert.Equal(ResumeState.Idle, t.Get(User, Device)!.State);
    }

    [Fact]
    public void Dismiss_And_NewMusic_ClearTheOffer()
    {
        var t = NewTracker();
        PlayMusicThenVideo(t);
        var end = T0.AddMinutes(30);
        t.NonAudioStopped(User, Device, end);
        Assert.True(t.Dismiss(User, Device));
        Assert.Null(t.GetOffer(User, Device, false, end.AddSeconds(10)));

        PlayMusicThenVideo(t);
        t.NonAudioStopped(User, Device, end);
        t.AudioActivity(User, Device, Queue(), Tracks[0], "p0", 0, end.AddSeconds(20)); // user started music by hand
        Assert.Equal(ResumeState.Idle, t.Get(User, Device)!.State);
    }

    [Fact]
    public void RepeatedTrack_IsResolvedByPlaylistItemId()
    {
        var t = NewTracker();
        var q = new List<QueueItemRef> { new(Tracks[0], "a"), new(Tracks[1], "b"), new(Tracks[0], "c") };
        t.AudioActivity(User, Device, q, Tracks[0], "c", 42, T0);
        Assert.Equal(2, t.Get(User, Device)!.CurrentIndex);
    }

    [Fact]
    public void StaleQueueWithoutCurrentTrack_KeepsCachedMusicQueue()
    {
        var t = NewTracker();
        t.AudioActivity(User, Device, Queue(), Tracks[0], "p0", 0, T0);
        var episode = Guid.NewGuid();
        t.AudioActivity(User, Device, [new QueueItemRef(episode, "v0")], Tracks[1], null, 7, T0.AddSeconds(30));

        var e = t.Get(User, Device)!;
        Assert.Equal(Tracks, e.ItemIds);
        Assert.Equal(1, e.CurrentIndex);
        Assert.Equal(7, e.PositionTicks);
    }

    [Fact]
    public void OtherDevices_AreIndependent()
    {
        var t = NewTracker();
        PlayMusicThenVideo(t);
        t.NonAudioStarted(User, "phone", T0.AddSeconds(61));
        Assert.Null(t.Get(User, "phone"));
        Assert.Equal(ResumeState.Pending, t.Get(User, Device)!.State);
    }

    [Fact]
    public void State_SurvivesRestart_ThroughJsonStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "tunabackin-test-" + Guid.NewGuid().ToString("N"), "snapshots.json");
        try
        {
            var t = new ResumeTracker(new JsonSnapshotStore(path), () => TrackerTiming.Default);
            PlayMusicThenVideo(t);

            var reloaded = new ResumeTracker(new JsonSnapshotStore(path), () => TrackerTiming.Default);
            var e = reloaded.Get(User, Device)!;
            Assert.Equal(ResumeState.Pending, e.State);
            Assert.Equal(Tracks, e.ItemIds);
            Assert.Equal(1, e.CurrentIndex);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ProgressTicks_DoNotWriteToDisk()
    {
        var t = NewTracker();
        t.AudioActivity(User, Device, Queue(), Tracks[0], "p0", 0, T0);
        t.AudioActivity(User, Device, null, Tracks[0], "p0", 10, T0.AddSeconds(10));
        Assert.Equal(0, _store.Saves);
    }

    private sealed class MemoryStore : ISnapshotStore
    {
        public int Saves { get; private set; }

        public IReadOnlyCollection<ResumeEntry> Load() => [];

        public void Save(IReadOnlyCollection<ResumeEntry> entries) => Saves++;
    }
}
