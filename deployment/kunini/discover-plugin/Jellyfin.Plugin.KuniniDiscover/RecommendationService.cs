// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Resolves external recommendations and excludes every matching local provider identifier.
/// </summary>
public sealed class RecommendationService
{
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbRecommendationClient _tmdbClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="tmdbClient">The TMDb client.</param>
    public RecommendationService(ILibraryManager libraryManager, TmdbRecommendationClient tmdbClient)
    {
        _libraryManager = libraryManager;
        _tmdbClient = tmdbClient;
    }

    /// <summary>
    /// Gets recommendations for a local movie or series.
    /// </summary>
    /// <param name="itemId">The local Jellyfin item identifier.</param>
    /// <param name="userId">The requesting Jellyfin user identifier.</param>
    /// <param name="limit">The maximum result count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A response, or <see langword="null"/> when the local item does not exist.</returns>
    public async Task<RecommendationResponse?> GetRecommendationsAsync(
        Guid itemId,
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, userId);
        if (item is null)
        {
            return null;
        }

        var (kind, itemKind) = item switch
        {
            Movie => (RecommendationMediaKind.Movie, BaseItemKind.Movie),
            Series => (RecommendationMediaKind.Series, BaseItemKind.Series),
            _ => ((RecommendationMediaKind?)null, (BaseItemKind?)null)
        };

        if (kind is null
            || itemKind is null
            || !item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbIdValue)
            || !int.TryParse(tmdbIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId)
            || tmdbId <= 0)
        {
            return new RecommendationResponse("TMDb", []);
        }

        var localIds = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [itemKind.Value],
                Recursive = true
            })
            .Select(localItem => localItem.TryGetProviderId(MetadataProvider.Tmdb, out var providerId) ? providerId : null)
            .OfType<string>()
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .ToHashSet(StringComparer.Ordinal);
        var candidates = await _tmdbClient.GetRecommendationsAsync(kind.Value, tmdbId, cancellationToken).ConfigureAwait(false);
        var items = RecommendationSelector.Select(candidates, kind.Value, localIds, tmdbId, limit);
        return new RecommendationResponse("TMDb", items);
    }

    /// <summary>
    /// Searches the external catalog and removes movies and series already present locally.
    /// </summary>
    /// <param name="query">The normalized search query.</param>
    /// <param name="limit">The maximum result count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>External movies and series absent from the Jellyfin library.</returns>
    public async Task<RecommendationResponse> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var localItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true
        });
        var localMovieIds = LocalTmdbIds(localItems.OfType<Movie>());
        var localSeriesIds = LocalTmdbIds(localItems.OfType<Series>());
        var candidates = await _tmdbClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        var items = RecommendationSelector.SelectSearch(candidates, localMovieIds, localSeriesIds, limit);
        return new RecommendationResponse("TMDb", items);
    }

    /// <summary>
    /// Gets full metadata for one external recommendation.
    /// </summary>
    /// <param name="sourceId">The TMDb identifier.</param>
    /// <param name="kind">The media kind.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The external details, or <see langword="null"/> when unavailable.</returns>
    internal async Task<ExternalDetailResponse?> GetExternalDetailsAsync(
        int sourceId,
        RecommendationMediaKind kind,
        CancellationToken cancellationToken)
    {
        var details = await _tmdbClient.GetDetailsAsync(kind, sourceId, cancellationToken).ConfigureAwait(false);
        return details is null ? null : RecommendationSelector.SelectDetails(details, kind);
    }

    private static HashSet<string> LocalTmdbIds(IEnumerable<BaseItem> items)
        => items
            .Select(item => item.TryGetProviderId(MetadataProvider.Tmdb, out var providerId) ? providerId : null)
            .OfType<string>()
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .ToHashSet(StringComparer.Ordinal);
}
