// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Serves the Kunini Discover web assets and recommendation API.
/// </summary>
[ApiController]
[Route("KuniniDiscover")]
public sealed class KuniniDiscoverController : ControllerBase
{
    private readonly RecommendationService _recommendationService;
    private readonly TmdbPosterClient _posterClient;
    private readonly ILogger<KuniniDiscoverController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KuniniDiscoverController"/> class.
    /// </summary>
    /// <param name="recommendationService">The recommendation service.</param>
    /// <param name="posterClient">The validated TMDb poster proxy client.</param>
    /// <param name="logger">The logger.</param>
    public KuniniDiscoverController(
        RecommendationService recommendationService,
        TmdbPosterClient posterClient,
        ILogger<KuniniDiscoverController> logger)
    {
        _recommendationService = recommendationService;
        _posterClient = posterClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns the detail-page integration script.
    /// </summary>
    /// <returns>The embedded JavaScript asset.</returns>
    [HttpGet("script.js")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult Script() => EmbeddedAsset("kunini-discover.js", "text/javascript; charset=utf-8");

    /// <summary>
    /// Returns the detail-page integration stylesheet.
    /// </summary>
    /// <returns>The embedded CSS asset.</returns>
    [HttpGet("styles.css")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult Styles() => EmbeddedAsset("kunini-discover.css", "text/css; charset=utf-8");

    /// <summary>
    /// Returns a TMDb poster through the server so browsers do not depend on direct CDN access.
    /// </summary>
    /// <param name="path">The TMDb poster path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="size">The TMDb image size.</param>
    /// <returns>The poster or backdrop as a same-origin image.</returns>
    [HttpGet("poster")]
    [AllowAnonymous]
    [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Poster(
        [FromQuery] string path,
        [FromQuery] string size = "w500",
        CancellationToken cancellationToken = default)
    {
        if (!TmdbPosterClient.IsValidPosterPath(path) || !TmdbPosterClient.IsValidSize(size))
        {
            return BadRequest();
        }

        try
        {
            var image = await _posterClient.GetPosterAsync(path, size, cancellationToken).ConfigureAwait(false);
            return File(image.Bytes, image.ContentType);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "TMDb poster proxy failed for {PosterPath}", path);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "TMDb poster is temporarily unavailable");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "TMDb poster proxy timed out for {PosterPath}", path);
            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "TMDb poster proxy timed out");
        }
    }

    /// <summary>
    /// Gets full metadata for one external TMDb recommendation.
    /// </summary>
    /// <param name="sourceId">The TMDb identifier.</param>
    /// <param name="mediaType">Movie or Series.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The external metadata rendered by the Jellyfin-style detail page.</returns>
    [HttpGet("details")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Details(
        [FromQuery] int sourceId,
        [FromQuery] string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (sourceId <= 0
            || !Enum.TryParse<RecommendationMediaKind>(mediaType, true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return BadRequest();
        }

        try
        {
            var result = await _recommendationService
                .GetExternalDetailsAsync(sourceId, kind, cancellationToken)
                .ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "TMDb details failed for {MediaType} {SourceId}", kind, sourceId);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "TMDb details are temporarily unavailable");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "TMDb details timed out for {MediaType} {SourceId}", kind, sourceId);
            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "TMDb details timed out");
        }
    }

    /// <summary>
    /// Gets TMDb recommendations that are not present in the library.
    /// </summary>
    /// <param name="itemId">The local Jellyfin movie or series identifier.</param>
    /// <param name="limit">The maximum result count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The external recommendations.</returns>
    [HttpGet("recommendations")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Recommendations(
        [FromQuery] Guid itemId,
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var userIdValue = User.Claims
            .FirstOrDefault(claim => claim.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await _recommendationService
                .GetRecommendationsAsync(itemId, userId, Math.Clamp(limit, 1, 20), cancellationToken)
                .ConfigureAwait(false);
            return result is null ? NotFound() : Ok(result);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "TMDb recommendations failed for Jellyfin item {ItemId}", itemId);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "TMDb recommendations are temporarily unavailable");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "TMDb recommendations timed out for Jellyfin item {ItemId}", itemId);
            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "TMDb recommendations timed out");
        }
    }

    private static ActionResult EmbeddedAsset(string fileName, string contentType)
    {
        var resourceName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Web.{1}",
            typeof(KuniniDiscoverController).Namespace,
            fileName);
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream is null ? new NotFoundResult() : new FileStreamResult(stream, contentType);
    }
}
