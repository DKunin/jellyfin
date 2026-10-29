// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace Jellyfin.Plugin.KuniniDiscover;

internal static class RecommendationSelector
{
    private const string PosterProxyPath = "/KuniniDiscover/poster?path=";

    private static readonly IReadOnlyDictionary<int, string> Genres = new Dictionary<int, string>
    {
        [12] = "Adventure", [14] = "Fantasy", [16] = "Animation", [18] = "Drama",
        [27] = "Horror", [28] = "Action", [35] = "Comedy", [36] = "History",
        [37] = "Western", [53] = "Thriller", [80] = "Crime", [99] = "Documentary",
        [878] = "Science fiction", [9648] = "Mystery", [10402] = "Music",
        [10749] = "Romance", [10751] = "Family", [10752] = "War",
        [10759] = "Action & adventure", [10762] = "Kids", [10763] = "News",
        [10764] = "Reality", [10765] = "Sci-fi & fantasy", [10766] = "Soap",
        [10767] = "Talk", [10768] = "War & politics", [10770] = "TV movie"
    };

    public static IReadOnlyList<RecommendationItem> Select(
        IReadOnlyList<TmdbCandidate> candidates,
        RecommendationMediaKind kind,
        IReadOnlySet<string> localTmdbIds,
        int currentTmdbId,
        int limit)
    {
        var selected = new List<RecommendationItem>(limit);
        var seen = new HashSet<int> { currentTmdbId };

        foreach (var candidate in candidates)
        {
            if (selected.Count >= limit
                || candidate.Id <= 0
                || !seen.Add(candidate.Id)
                || localTmdbIds.Contains(candidate.Id.ToString(CultureInfo.InvariantCulture)))
            {
                continue;
            }

            var title = kind == RecommendationMediaKind.Movie ? candidate.MovieTitle : candidate.SeriesTitle;
            var date = kind == RecommendationMediaKind.Movie ? candidate.MovieReleaseDate : candidate.SeriesReleaseDate;
            if (string.IsNullOrWhiteSpace(title) || !TryReadYear(date, out var year))
            {
                continue;
            }

            var genre = candidate.GenreIds
                .Select(id => Genres.TryGetValue(id, out var value) ? value : null)
                .FirstOrDefault(value => value is not null)
                ?? "Recommended by TMDb";
            var posterUrl = BuildImageUrl(candidate.PosterPath, null);
            var matchPercent = Math.Max(60, 96 - (selected.Count * 3));

            selected.Add(new RecommendationItem(
                candidate.Id,
                kind.ToString(),
                title.Trim(),
                year,
                posterUrl,
                candidate.Overview,
                genre,
                matchPercent));
        }

        return selected;
    }

    public static ExternalDetailResponse? SelectDetails(TmdbDetails details, RecommendationMediaKind kind)
    {
        var title = kind == RecommendationMediaKind.Movie ? details.MovieTitle : details.SeriesTitle;
        var originalTitle = kind == RecommendationMediaKind.Movie ? details.OriginalMovieTitle : details.OriginalSeriesTitle;
        var releaseDate = kind == RecommendationMediaKind.Movie ? details.MovieReleaseDate : details.SeriesReleaseDate;
        if (details.Id <= 0 || string.IsNullOrWhiteSpace(title) || !TryReadYear(releaseDate, out var year))
        {
            return null;
        }

        var episodeRuntime = details.EpisodeRuntimeMinutes.FirstOrDefault(value => value > 0);
        var runtime = kind == RecommendationMediaKind.Movie
            ? details.MovieRuntimeMinutes
            : episodeRuntime > 0
                ? episodeRuntime
                : details.LastEpisodeToAir?.RuntimeMinutes;
        var certification = kind == RecommendationMediaKind.Movie
            ? SelectMovieCertification(details.MovieReleaseDates)
            : SelectSeriesCertification(details.SeriesContentRatings);
        var genres = details.Genres
            .Select(genre => genre.Name?.Trim())
            .OfType<string>()
            .Where(name => name.Length > 0)
            .ToArray();
        var trailerKey = SelectTrailerKey(details.Videos);

        return new ExternalDetailResponse(
            details.Id,
            kind.ToString(),
            title.Trim(),
            string.Equals(title.Trim(), originalTitle?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : originalTitle?.Trim(),
            year,
            releaseDate,
            runtime is > 0 ? runtime : null,
            kind == RecommendationMediaKind.Series && details.SeasonCount is > 0 ? details.SeasonCount : null,
            certification,
            Math.Round(Math.Max(0, details.Rating), 1, MidpointRounding.AwayFromZero),
            Math.Max(0, details.VoteCount),
            string.IsNullOrWhiteSpace(details.Tagline) ? null : details.Tagline.Trim(),
            details.Overview?.Trim() ?? string.Empty,
            genres,
            string.IsNullOrWhiteSpace(details.Status) ? null : details.Status.Trim(),
            BuildImageUrl(details.PosterPath, null),
            BuildImageUrl(details.BackdropPath, "w1280"),
            trailerKey is null ? null : $"https://www.youtube.com/watch?v={trailerKey}",
            trailerKey is null ? null : $"https://www.youtube-nocookie.com/embed/{trailerKey}?autoplay=0&playsinline=1&rel=0");
    }

    internal static bool TryReadYear(string? date, out int year)
    {
        year = 0;
        return date is { Length: >= 4 }
            && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && year is >= 1888 and <= 3000;
    }

    private static string? BuildImageUrl(string? path, string? size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var url = PosterProxyPath + Uri.EscapeDataString(path);
        return size is null ? url : url + "&size=" + Uri.EscapeDataString(size);
    }

    private static string? SelectMovieCertification(TmdbReleaseDateCollection? collection)
    {
        var countries = collection?.Results ?? [];
        return countries
            .OrderByDescending(country => string.Equals(country.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
            .SelectMany(country => country.ReleaseDates)
            .Select(entry => entry.Certification?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? SelectSeriesCertification(TmdbContentRatingCollection? collection)
    {
        var ratings = collection?.Results ?? [];
        return ratings
            .OrderByDescending(rating => string.Equals(rating.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
            .Select(rating => rating.Rating?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? SelectTrailerKey(TmdbVideoCollection? collection)
    {
        return (collection?.Results ?? [])
            .Where(video => string.Equals(video.Site, "YouTube", StringComparison.OrdinalIgnoreCase))
            .Where(video => string.Equals(video.Type, "Trailer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(video.Type, "Teaser", StringComparison.OrdinalIgnoreCase))
            .Where(video => IsValidYouTubeKey(video.Key))
            .OrderBy(video => string.Equals(video.Type, "Trailer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(video => video.Official)
            .ThenByDescending(video => string.Equals(video.Language, "en", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(video => video.PublishedAt)
            .Select(video => video.Key!.Trim())
            .FirstOrDefault();
    }

    private static bool IsValidYouTubeKey(string? key)
    {
        return key is { Length: >= 6 and <= 20 }
            && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}
