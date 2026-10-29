// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace Jellyfin.Plugin.KuniniDiscover;

internal static class RecommendationSelector
{
    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w500";
    private const string TmdbBaseUrl = "https://www.themoviedb.org";

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

            var mediaPath = kind == RecommendationMediaKind.Movie ? "movie" : "tv";
            var genre = candidate.GenreIds
                .Select(id => Genres.TryGetValue(id, out var value) ? value : null)
                .FirstOrDefault(value => value is not null)
                ?? "Recommended by TMDb";
            var posterUrl = string.IsNullOrWhiteSpace(candidate.PosterPath) ? null : PosterBaseUrl + candidate.PosterPath;
            var matchPercent = Math.Max(60, 96 - (selected.Count * 3));

            selected.Add(new RecommendationItem(
                candidate.Id,
                kind.ToString(),
                title.Trim(),
                year,
                posterUrl,
                string.Create(CultureInfo.InvariantCulture, $"{TmdbBaseUrl}/{mediaPath}/{candidate.Id}"),
                candidate.Overview,
                genre,
                matchPercent));
        }

        return selected;
    }

    private static bool TryReadYear(string? date, out int year)
    {
        year = 0;
        return date is { Length: >= 4 }
            && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && year is >= 1888 and <= 3000;
    }
}
