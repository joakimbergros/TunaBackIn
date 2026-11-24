using Jellyfin.Plugin.TunaBackIn.Core;
using Jellyfin.Plugin.TunaBackIn.Web;
using Xunit;

namespace Jellyfin.Plugin.TunaBackIn.Tests;

public class QueueTitleAndInjectionTests
{
    [Fact]
    public void SingleAlbum() => Assert.Equal("Blue Train", QueueTitle.Build(["Blue Train", "blue train", "Blue Train"], 0, "Moment's Notice"));

    [Fact]
    public void MixedQueue() => Assert.Equal("Kind of Blue +2 more", QueueTitle.Build(["Blue Train", "Kind of Blue", null, "Giant Steps"], 1, "So What"));

    [Fact]
    public void NoAlbums_FallsBackToTrack() => Assert.Equal("So What", QueueTitle.Build([null, null], 0, "So What"));

    [Theory]
    [InlineData("/web/", true)]
    [InlineData("/web", true)]
    [InlineData("/web/index.html", true)]
    [InlineData("/jellyfin/web/index.html", true)]
    [InlineData("/web/main.jellyfin.bundle.js", false)]
    [InlineData("/Users/Me", false)]
    public void IndexPath(string path, bool expected) => Assert.Equal(expected, ScriptInjectionMiddleware.IsIndexPath(path));

    [Fact]
    public void Inject_BeforeBodyClose_Once()
    {
        var once = ScriptInjectionMiddleware.Inject("<html><body><div></div></body></html>");
        Assert.Equal("<html><body><div></div>" + ScriptInjectionMiddleware.ScriptTag + "</body></html>", once);
        Assert.Equal(once, ScriptInjectionMiddleware.Inject(once));
    }
}
