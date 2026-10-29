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

var details = RecommendationSelector.SelectDetails(
    new TmdbDetails
    {
        Id = 300,
        MovieTitle = "External",
        OriginalMovieTitle = "External Original",
        MovieReleaseDate = "2022-01-01",
        MovieRuntimeMinutes = 128,
        Rating = 7.84,
        VoteCount = 1234,
        Tagline = "Beyond the library.",
        Overview = "A complete external synopsis.",
        Status = "Released",
        PosterPath = "/poster.jpg",
        BackdropPath = "/backdrop.jpg",
        Genres = [new TmdbGenre { Name = "Science Fiction" }, new TmdbGenre { Name = "Drama" }],
        MovieReleaseDates = new TmdbReleaseDateCollection
        {
            Results =
            [
                new TmdbReleaseCountry
                {
                    CountryCode = "US",
                    ReleaseDates = [new TmdbReleaseDateEntry { Certification = "PG-13" }]
                }
            ]
        },
        Videos = new TmdbVideoCollection
        {
            Results =
            [
                new TmdbVideo { Key = "vimeo-ignored", Site = "Vimeo", Type = "Trailer", Official = true },
                new TmdbVideo { Key = "Bad key!", Site = "YouTube", Type = "Trailer", Official = true },
                new TmdbVideo { Key = "TeaserKey01", Site = "YouTube", Type = "Teaser", Official = true, Language = "en" },
                new TmdbVideo { Key = "OtherKey001", Site = "YouTube", Type = "Trailer", Official = false, Language = "en" },
                new TmdbVideo { Key = "8g18jFHCLXk", Site = "YouTube", Type = "Trailer", Official = true, Language = "en" }
            ]
        }
    },
    RecommendationMediaKind.Movie)
    ?? throw new InvalidOperationException("full external details must be selected");
Assert(details.Title == "External" && details.OriginalTitle == "External Original", "detail titles must be preserved");
Assert(details.RuntimeMinutes == 128 && details.Certification == "PG-13", "runtime and certification must be preserved");
Assert(details.Rating == 7.8 && details.VoteCount == 1234, "rating metadata must be normalized");
Assert(details.Genres.SequenceEqual(["Science Fiction", "Drama"]), "detail genres must be preserved");
Assert(details.BackdropUrl == "/KuniniDiscover/poster?path=%2Fbackdrop.jpg&size=w1280", "backdrops must use the larger same-origin image proxy");
Assert(details.TrailerWatchUrl == "https://www.youtube.com/watch?v=8g18jFHCLXk", "the preferred YouTube trailer must expose a safe watch URL");
Assert(details.TrailerEmbedUrl == "https://www.youtube-nocookie.com/embed/8g18jFHCLXk?autoplay=0&playsinline=1&rel=0", "trailers must use the privacy-enhanced non-autoplay embed URL");

var seriesDetails = RecommendationSelector.SelectDetails(
    new TmdbDetails
    {
        Id = 500,
        SeriesTitle = "External series",
        SeriesReleaseDate = "2001-10-02",
        SeasonCount = 9,
        LastEpisodeToAir = new TmdbEpisode { RuntimeMinutes = 24 },
        SeriesContentRatings = new TmdbContentRatingCollection
        {
            Results = [new TmdbContentRating { CountryCode = "US", Rating = "TV-14" }]
        },
        Videos = new TmdbVideoCollection
        {
            Results = [new TmdbVideo { Key = "javascript:bad", Site = "YouTube", Type = "Trailer", Official = true }]
        }
    },
    RecommendationMediaKind.Series)
    ?? throw new InvalidOperationException("full external series details must be selected");
Assert(seriesDetails.RuntimeMinutes == 24, "series runtime must fall back to the latest episode when TMDb omits episode_run_time");
Assert(seriesDetails.SeasonCount == 9 && seriesDetails.Certification == "TV-14", "series seasons and content rating must be preserved");
Assert(seriesDetails.TrailerWatchUrl is null && seriesDetails.TrailerEmbedUrl is null, "invalid trailer keys must not reach the browser");

Assert(TmdbPosterClient.IsValidPosterPath("/valid_TMDb-poster.jpg"), "valid TMDb poster paths must be accepted");
Assert(!TmdbPosterClient.IsValidPosterPath("https://example.com/poster.jpg"), "absolute poster URLs must be rejected");
Assert(!TmdbPosterClient.IsValidPosterPath("/../poster.jpg"), "path traversal must be rejected");
Assert(TmdbPosterClient.IsValidSize("w500") && TmdbPosterClient.IsValidSize("w1280"), "supported TMDb image sizes must be accepted");
Assert(!TmdbPosterClient.IsValidSize("original"), "unbounded TMDb image sizes must be rejected");

var resources = typeof(RecommendationSelector).Assembly.GetManifestResourceNames();
Assert(resources.Contains("Jellyfin.Plugin.KuniniDiscover.Web.kunini-discover.js", StringComparer.Ordinal), "the browser script must be embedded in the plugin assembly");
Assert(resources.Contains("Jellyfin.Plugin.KuniniDiscover.Web.kunini-discover.css", StringComparer.Ordinal), "the browser stylesheet must be embedded in the plugin assembly");

Console.WriteLine("Kunini Discover tests passed: movie/series filtering, metadata, embedded assets");
