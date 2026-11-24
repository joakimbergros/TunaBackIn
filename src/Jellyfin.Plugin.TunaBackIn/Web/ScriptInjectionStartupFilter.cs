using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.TunaBackIn.Web;

/// <summary>
/// Puts <see cref="ScriptInjectionMiddleware"/> in front of Jellyfin's pipeline.
/// </summary>
public class ScriptInjectionStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ScriptInjectionMiddleware>();
            next(app);
        };
    }
}
