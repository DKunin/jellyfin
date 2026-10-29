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
    private readonly TrailerTokenService _trailerTokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="tmdbClient">The TMDb client.</param>
    /// <param name="trailerTokenService">The short-lived trailer URL signer.</param>
    public RecommendationService(
        ILibraryManager libraryManager,
        TmdbRecommendationClient tmdbClient,
        TrailerTokenService trailerTokenService)
    {
        _libraryManager = libraryManager;
        _tmdbClient = tmdbClient;
        _trailerTokenService = trailerTokenService;
    }

    /// <summary>
    /// Gets recommendations for a local movie or series, or external movie credits for a person.
    /// </summary>
    /// <param name="itemId">The local Jellyfin item identifier.</param>
    /// <param name="userId">The requesting Jellyfin user identifier.</param>
    /// <param name="limit">The maximum result count.</param>
    /// <param name="page">The one-based external result page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A response, or <see langword="null"/> when the local item does not exist.</returns>
    public async Task<RecommendationResponse?> GetRecommendationsAsync(
        Guid itemId,
        Guid userId,
        int limit,
        int page,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, userId);
        if (item is null)
        {
            return null;
        }

        if (item is Person person)
        {
            if (!person.TryGetProviderId(MetadataProvider.Tmdb, out var personTmdbIdValue)
                || !int.TryParse(personTmdbIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var personTmdbId)
                || personTmdbId <= 0)
            {
                return new RecommendationResponse("TMDb", []);
            }

            var localMovieIds = LocalTmdbIds(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true
            }).OfType<Movie>());
            var personDetails = await _tmdbClient.GetPersonDetailsAsync(personTmdbId, cancellationToken).ConfigureAwait(false);
            return personDetails is null
                ? new RecommendationResponse("TMDb", [])
                : RecommendationSelector.SelectFilmography(personDetails, person.Name, localMovieIds, limit);
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
        var candidatePage = await _tmdbClient.GetRecommendationsAsync(kind.Value, tmdbId, page, cancellationToken).ConfigureAwait(false);
        var items = RecommendationSelector.Select(candidatePage.Results, kind.Value, localIds, tmdbId, limit);
        var currentPage = candidatePage.Page > 0 ? candidatePage.Page : page;
        return new RecommendationResponse(
            "TMDb",
            items,
            Page: currentPage,
            HasMore: currentPage < candidatePage.TotalPages);
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
        var selected = details is null ? null : RecommendationSelector.SelectDetails(details, kind);
        if (selected?.TrailerWatchUrl is null)
        {
            return selected;
        }

        var videoId = new Uri(selected.TrailerWatchUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(part => part.Length == 2 && part[0] == "v")?[1];
        return selected with { TrailerProxyUrl = _trailerTokenService.CreatePath(videoId) };
    }

    private static HashSet<string> LocalTmdbIds(IEnumerable<BaseItem> items)
        => items
            .Select(item => item.TryGetProviderId(MetadataProvider.Tmdb, out var providerId) ? providerId : null)
            .OfType<string>()
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .ToHashSet(StringComparer.Ordinal);
}
