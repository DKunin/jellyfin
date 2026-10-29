// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Fetches recommendations from the free TMDb API.
/// </summary>
public sealed class TmdbRecommendationClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbRecommendationClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured TMDb HTTP client.</param>
    /// <param name="cache">The in-process response cache.</param>
    public TmdbRecommendationClient(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    /// <summary>
    /// Gets one TMDb recommendation page.
    /// </summary>
    /// <param name="kind">The source media kind.</param>
    /// <param name="tmdbId">The source TMDb identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered TMDb candidates.</returns>
    internal async Task<IReadOnlyList<TmdbCandidate>> GetRecommendationsAsync(
        RecommendationMediaKind kind,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"kunini-discover:{kind}:{tmdbId}");
        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var mediaPath = kind == RecommendationMediaKind.Movie ? "movie" : "tv";
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mediaPath}/{tmdbId}/recommendations?api_key={Uri.EscapeDataString(DiscoverSettings.TmdbApiKey)}&language=en-US&page=1");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var page = await response.Content.ReadFromJsonAsync<TmdbPage>(cancellationToken: cancellationToken).ConfigureAwait(false);
                return page?.Results ?? [];
            }).ConfigureAwait(false);

        return result ?? [];
    }
}
