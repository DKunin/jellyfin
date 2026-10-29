// SPDX-License-Identifier: GPL-2.0-only

using Jellyfin.Plugin.KuniniDiscover;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var candidates = new List<TmdbCandidate>
{
    new() { Id = 100, MovieTitle = "Current", MovieReleaseDate = "2024-03-01", GenreIds = [878] },
    new() { Id = 200, MovieTitle = "Already local", MovieReleaseDate = "2023-02-01", GenreIds = [18] },
    new() { Id = 300, MovieTitle = "External", MovieReleaseDate = "2022-01-01", PosterPath = "/poster.jpg", GenreIds = [878] },
    new() { Id = 300, MovieTitle = "External duplicate", MovieReleaseDate = "2022-01-01", GenreIds = [878] },
    new() { Id = 400, MovieTitle = "No release year", MovieReleaseDate = string.Empty, GenreIds = [28] }
};
var selected = RecommendationSelector.Select(
    candidates,
    RecommendationMediaKind.Movie,
    new HashSet<string>(StringComparer.Ordinal) { "200" },
    currentTmdbId: 100,
    limit: 10);

Assert(selected.Count == 1, "only one external recommendation with a release year must remain");
Assert(selected[0].SourceId == 300, "the external TMDb id must be preserved");
Assert(selected[0].Title == "External" && selected[0].Year == 2022, "title and release year must be preserved");
Assert(selected[0].Genre == "Science fiction", "the primary TMDb genre must be mapped");
Assert(selected[0].PosterUrl == "/KuniniDiscover/poster?path=%2Fposter.jpg", "poster paths must use the validated same-origin proxy");
Assert(selected[0].MatchPercent == 96, "the first recommendation must receive the strongest rank score");

var seriesSelected = RecommendationSelector.Select(
    [new TmdbCandidate { Id = 500, SeriesTitle = "External series", SeriesReleaseDate = "2001-10-02", GenreIds = [10765] }],
    RecommendationMediaKind.Series,
    new HashSet<string>(StringComparer.Ordinal),
    currentTmdbId: 600,
    limit: 10);
Assert(seriesSelected.Count == 1, "a series recommendation must be selected");
Assert(seriesSelected[0].MediaType == "Series" && seriesSelected[0].Year == 2001, "series metadata must use name and first_air_date");
Assert(seriesSelected[0].DetailsUrl == "https://www.themoviedb.org/tv/500", "series details must use the TMDb TV route");
Assert(TmdbPosterClient.IsValidPosterPath("/valid_TMDb-poster.jpg"), "valid TMDb poster paths must be accepted");
Assert(!TmdbPosterClient.IsValidPosterPath("https://example.com/poster.jpg"), "absolute poster URLs must be rejected");
Assert(!TmdbPosterClient.IsValidPosterPath("/../poster.jpg"), "path traversal must be rejected");

var resources = typeof(RecommendationSelector).Assembly.GetManifestResourceNames();
Assert(resources.Contains("Jellyfin.Plugin.KuniniDiscover.Web.kunini-discover.js", StringComparer.Ordinal), "the browser script must be embedded in the plugin assembly");
Assert(resources.Contains("Jellyfin.Plugin.KuniniDiscover.Web.kunini-discover.css", StringComparer.Ordinal), "the browser stylesheet must be embedded in the plugin assembly");

Console.WriteLine("Kunini Discover tests passed: movie/series filtering, metadata, embedded assets");
