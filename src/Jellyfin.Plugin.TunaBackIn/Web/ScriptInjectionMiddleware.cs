using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.TunaBackIn.Web;

/// <summary>
/// Adds the TunaBackIn client script to jellyfin-web's index.html, in memory.
/// </summary>
public class ScriptInjectionMiddleware
{
    /// <summary>The tag inserted before &lt;/body&gt;. Relative to /web/ so it works with a base URL.</summary>
    internal const string ScriptTag = "<script defer src=\"../TunaBackIn/client.js\"></script>";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    public ScriptInjectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!HttpMethods.IsGet(context.Request.Method) || !IsIndexPath(context.Request.Path.Value))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Get an uncompressed, full body we can edit.
        var headers = context.Request.Headers;
        headers.Remove(HeaderNames.AcceptEncoding);
        headers.Remove(HeaderNames.IfNoneMatch);
        headers.Remove(HeaderNames.IfModifiedSince);
        headers.Remove(HeaderNames.Range);

        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        buffer.Position = 0;
        var response = context.Response;
        var isHtml = response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
        if (response.StatusCode != StatusCodes.Status200OK || !isHtml || response.Headers.ContainsKey(HeaderNames.ContentEncoding))
        {
            await buffer.CopyToAsync(original).ConfigureAwait(false);
            return;
        }

        var html = Inject(Encoding.UTF8.GetString(buffer.ToArray()));
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength = bytes.Length;
        response.Headers.Remove(HeaderNames.ETag);
        response.Headers.Remove(HeaderNames.LastModified);
        await original.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>Whether the path is jellyfin-web's index page (with or without a base URL).</summary>
    /// <param name="path">The request path.</param>
    /// <returns>True for /web, /web/ and /web/index.html.</returns>
    internal static bool IsIndexPath(string? path) =>
        path is not null
        && (path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase));

    /// <summary>Inserts the script tag before &lt;/body&gt;, once.</summary>
    /// <param name="html">The page.</param>
    /// <returns>The page with the script.</returns>
    internal static string Inject(string html)
    {
        if (html.Contains("TunaBackIn/client.js", StringComparison.Ordinal))
        {
            return html;
        }

        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html + ScriptTag : html.Insert(end, ScriptTag);
    }
}
