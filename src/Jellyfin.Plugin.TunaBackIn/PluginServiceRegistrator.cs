using System.IO;
using Jellyfin.Plugin.TunaBackIn.Configuration;
using Jellyfin.Plugin.TunaBackIn.Core;
using Jellyfin.Plugin.TunaBackIn.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TunaBackIn;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<IApplicationPaths>();
            var store = new JsonSnapshotStore(Path.Combine(paths.DataPath, "tunabackin", "snapshots.json"));
            return new ResumeTracker(store, () => (Plugin.Instance?.Configuration ?? new PluginConfiguration()).ToTiming());
        });
        serviceCollection.AddSingleton<ResumeService>();
        serviceCollection.AddHostedService<PlaybackMonitor>();
        serviceCollection.AddTransient<IStartupFilter, ScriptInjectionStartupFilter>();
    }
}
