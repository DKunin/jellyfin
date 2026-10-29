// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Retrieves TMDb posters through a fixed public image proxy and caches them in process.
/// </summary>
public sealed class TmdbPosterClient
{
    private const int MaximumPosterBytes = 3 * 1024 * 1024;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbPosterClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured image proxy HTTP client.</param>
    /// <param name="cache">The in-process image cache.</param>
    public TmdbPosterClient(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    /// <summary>
    /// Gets one TMDb poster as a JPEG.
    /// </summary>
    /// <param name="posterPath">The validated TMDb poster path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The poster bytes and verified image media type.</returns>
    public async Task<PosterImage> GetPosterAsync(string posterPath, CancellationToken cancellationToken)
    {
        if (!IsValidPosterPath(posterPath))
        {
            throw new ArgumentException("Invalid TMDb poster path.", nameof(posterPath));
        }

        var cacheKey = "kunini-discover:poster:" + posterPath;
        var image = await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"image.tmdb.org/t/p/w500{posterPath}");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (bytes.Length is 0 or > MaximumPosterBytes
                    || contentType is not ("image/jpeg" or "image/png" or "image/webp"))
                {
                    throw new HttpRequestException("Poster proxy returned invalid image content.");
                }

                return new PosterImage(bytes, contentType);
            }).ConfigureAwait(false);

        return image ?? throw new HttpRequestException("Poster proxy returned no image.");
    }

    internal static bool IsValidPosterPath(string? posterPath)
    {
        if (posterPath is not { Length: >= 6 and <= 160 }
            || posterPath[0] != '/'
            || !(posterPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || posterPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return posterPath.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.") < 0;
    }
}

/// <summary>
/// A verified poster payload.
/// </summary>
/// <param name="Bytes">The poster bytes.</param>
/// <param name="ContentType">The verified image media type.</param>
public sealed record PosterImage(byte[] Bytes, string ContentType);
