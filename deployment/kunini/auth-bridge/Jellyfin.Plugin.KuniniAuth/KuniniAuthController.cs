// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Supplies the web login guard and the currently verified identity.
/// </summary>
[ApiController]
[Route("KuniniAuth")]
public sealed class KuniniAuthController : ControllerBase
{
    /// <summary>
    /// Returns the browser bridge script.
    /// </summary>
    [HttpGet("script.js")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult Script()
    {
        var resourceName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Web.kunini-sso.js",
            typeof(KuniniAuthController).Namespace);
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream is null ? NotFound() : File(stream, "text/javascript; charset=utf-8");
    }

    /// <summary>
    /// Returns the Jellyfin account bound to the current verified Kunini identity.
    /// </summary>
    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
    {
        if (!KuniniAuthSettings.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!KuniniIdentity.TryRead(Request.Headers, out var identity) || identity is null)
        {
            return Unauthorized();
        }

        return Ok(new StatusResponse(true, Constants.LoginUsername, identity.JellyfinUsername));
    }

    private sealed record StatusResponse(
        [property: JsonPropertyName("enabled")] bool Enabled,
        [property: JsonPropertyName("loginUsername")] string LoginUsername,
        [property: JsonPropertyName("jellyfinUsername")] string JellyfinUsername);
}
