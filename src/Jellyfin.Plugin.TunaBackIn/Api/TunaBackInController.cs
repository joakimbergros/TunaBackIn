using System;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TunaBackIn.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Jellyfin.Plugin.TunaBackIn.Api;

/// <summary>
/// TunaBackIn endpoints.
/// </summary>
[ApiController]
[Route("TunaBackIn")]
[Authorize]
public class TunaBackInController : ControllerBase
{
    // Claim types issued by Jellyfin's auth handler (Jellyfin.Api InternalClaimTypes).
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string AdministratorRole = "Administrator";

    private readonly ResumeTracker _tracker;
    private readonly ResumeService _resumeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunaBackInController"/> class.
    /// </summary>
    /// <param name="tracker">The tracker.</param>
    /// <param name="resumeService">The resume service.</param>
    public TunaBackInController(ResumeTracker tracker, ResumeService resumeService)
    {
        _tracker = tracker;
        _resumeService = resumeService;
    }

    /// <summary>
    /// Serves the jellyfin-web client script.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.client.js");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, "application/javascript");
    }

    /// <summary>
    /// Gets the saved music snapshot for a device.
    /// </summary>
    /// <param name="deviceId">The device id (query string: jellyfin-web ids are base64 and may contain '/').</param>
    /// <returns>The snapshot.</returns>
    [HttpGet("Snapshot")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<ResumeEntry> GetSnapshot([FromQuery, BindRequired] string deviceId)
    {
        var entry = Resolve(deviceId);
        return entry is null ? NotFound() : entry;
    }

    /// <summary>
    /// Gets the resume offer for a device, if one should be shown now.
    /// </summary>
    /// <param name="deviceId">The device id (query string: jellyfin-web ids are base64 and may contain '/').</param>
    /// <returns>The offer, or 204.</returns>
    [HttpGet("Offer")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult<OfferDto> GetOffer([FromQuery, BindRequired] string deviceId)
    {
        if (Plugin.Instance?.Configuration.ShowPrompt == false)
        {
            return NoContent();
        }

        var entry = Resolve(deviceId);
        if (entry is null)
        {
            return NoContent();
        }

        var offer = _tracker.GetOffer(entry.UserId, deviceId, _resumeService.IsPlaying(entry.UserId, deviceId), DateTimeOffset.UtcNow);
        if (offer is null)
        {
            return NoContent();
        }

        if (offer.Title is null)
        {
            _resumeService.FillTitle(offer);
        }

        return new OfferDto(offer.Title ?? "your music", offer.ItemIds.Count, offer.CurrentIndex);
    }

    /// <summary>
    /// Resumes the saved music on the device.
    /// </summary>
    /// <param name="deviceId">The device id (query string: jellyfin-web ids are base64 and may contain '/').</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204 when sent.</returns>
    [HttpPost("Resume")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Resume([FromQuery, BindRequired] string deviceId, CancellationToken cancellationToken)
    {
        var entry = Resolve(deviceId);
        if (entry is null)
        {
            return NotFound();
        }

        return await _resumeService.ResumeAsync(entry.UserId, deviceId, cancellationToken).ConfigureAwait(false) switch
        {
            ResumeResult.Sent => NoContent(),
            ResumeResult.DeviceUnavailable => Conflict("The device has no active session that accepts remote control."),
            _ => NotFound()
        };
    }

    /// <summary>
    /// Dismisses the resume offer for a device.
    /// </summary>
    /// <param name="deviceId">The device id (query string: jellyfin-web ids are base64 and may contain '/').</param>
    /// <returns>204.</returns>
    [HttpDelete("Offer")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Dismiss([FromQuery, BindRequired] string deviceId)
    {
        var entry = Resolve(deviceId);
        if (entry is not null)
        {
            _tracker.Dismiss(entry.UserId, deviceId);
        }

        return NoContent();
    }

    /// <summary>
    /// Finds the entry for the device that the caller may see: their own, or (API key / admin) the most recent one.
    /// </summary>
    private ResumeEntry? Resolve(string deviceId)
    {
        var userIdValue = User.FindFirstValue(UserIdClaim);
        var isPrivileged = User.FindFirstValue(IsApiKeyClaim) == bool.TrueString || User.IsInRole(AdministratorRole);

        if (Guid.TryParse(userIdValue, out var userId) && !userId.Equals(Guid.Empty))
        {
            var own = _tracker.Get(userId, deviceId);
            if (own is not null || !isPrivileged)
            {
                return own;
            }
        }

        return isPrivileged
            ? _tracker.FindByDevice(deviceId).OrderByDescending(e => e.LastAudioActivity).FirstOrDefault()
            : null;
    }
}
