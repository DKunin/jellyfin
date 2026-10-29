// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Net;
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
    /// <param name="page">The one-based TMDb result page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered TMDb result page.</returns>
    internal async Task<TmdbPage> GetRecommendationsAsync(
        RecommendationMediaKind kind,
        int tmdbId,
        int page,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"kunini-discover:{kind}:{tmdbId}:page:{page}");
        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var mediaPath = kind == RecommendationMediaKind.Movie ? "movie" : "tv";
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mediaPath}/{tmdbId}/recommendations?api_key={Uri.EscapeDataString(DiscoverSettings.TmdbApiKey)}&language=en-US&page={page}");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TmdbPage>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return result ?? new TmdbPage { Page = page, TotalPages = page };
    }

    /// <summary>
    /// Searches TMDb for movies and series matching a user query.
    /// </summary>
    /// <param name="query">The normalized search query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered TMDb search candidates.</returns>
    internal async Task<IReadOnlyList<TmdbCandidate>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"kunini-discover:search:{query.ToUpperInvariant()}");
        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"search/multi?api_key={Uri.EscapeDataString(DiscoverSettings.TmdbApiKey)}&language=en-US&include_adult=false&page=1&query={Uri.EscapeDataString(query)}");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var page = await response.Content.ReadFromJsonAsync<TmdbPage>(cancellationToken: cancellationToken).ConfigureAwait(false);
                return page?.Results ?? [];
            }).ConfigureAwait(false);

        return result ?? [];
    }

    /// <summary>
    /// Gets a person's TMDb profile and movie credits.
    /// </summary>
    /// <param name="tmdbId">The TMDb person identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The person details, or <see langword="null"/> when the person does not exist.</returns>
    internal async Task<TmdbPersonDetails?> GetPersonDetailsAsync(
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"kunini-discover:person:{tmdbId}");
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"person/{tmdbId}?api_key={Uri.EscapeDataString(DiscoverSettings.TmdbApiKey)}&language=en-US&append_to_response=movie_credits");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TmdbPersonDetails>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets full TMDb metadata for one recommended movie or series.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="tmdbId">The TMDb identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TMDb details, or <see langword="null"/> when the title does not exist.</returns>
    internal async Task<TmdbDetails?> GetDetailsAsync(
        RecommendationMediaKind kind,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Create(CultureInfo.InvariantCulture, $"kunini-discover:details:{kind}:{tmdbId}");
        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var mediaPath = kind == RecommendationMediaKind.Movie ? "movie" : "tv";
                var append = kind == RecommendationMediaKind.Movie ? "release_dates,videos" : "content_ratings,videos";
                var requestUri = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{mediaPath}/{tmdbId}?api_key={Uri.EscapeDataString(DiscoverSettings.TmdbApiKey)}&language=en-US&append_to_response={append}");
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TmdbDetails>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}
