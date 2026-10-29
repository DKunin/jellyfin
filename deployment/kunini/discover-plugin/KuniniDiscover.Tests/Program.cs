// SPDX-License-Identifier: GPL-2.0-only

using System.Text.Json;
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

var searchSelected = RecommendationSelector.SelectSearch(
    [
        new TmdbCandidate { Id = 700, MediaType = "movie", MovieTitle = "External movie", MovieReleaseDate = "1980-05-09", GenreIds = [27] },
        new TmdbCandidate { Id = 701, MediaType = "tv", SeriesTitle = "Local series", SeriesReleaseDate = "2009-02-13", GenreIds = [18] },
        new TmdbCandidate { Id = 702, MediaType = "person", MovieTitle = "Ignored person", MovieReleaseDate = "1990-01-01" },
        new TmdbCandidate { Id = 703, MediaType = "tv", SeriesTitle = "External series", SeriesReleaseDate = "2024-01-01", GenreIds = [9648] }
    ],
    new HashSet<string>(StringComparer.Ordinal),
    new HashSet<string>(StringComparer.Ordinal) { "701" },
    limit: 10);
Assert(searchSelected.Count == 2, "external search must keep only non-local movies and series");
Assert(searchSelected[0].MediaType == "Movie" && searchSelected[0].Title == "External movie", "movie search metadata must be preserved");
Assert(searchSelected[0].MatchPercent == 0, "title search results must not invent recommendation match percentages");
Assert(searchSelected[1].MediaType == "Series" && searchSelected[1].SourceId == 703, "series search results must preserve their media kind and identifier");

var recommendationPage = JsonSerializer.Deserialize<TmdbPage>("""
    {"page":2,"total_pages":27,"results":[{"id":704,"title":"Next page","release_date":"2023-04-05"}]}
    """) ?? throw new InvalidOperationException("TMDb recommendation pages must deserialize");
Assert(recommendationPage.Page == 2 && recommendationPage.TotalPages == 27, "TMDb pagination metadata must be preserved");
Assert(recommendationPage.Results.Count == 1 && recommendationPage.Results[0].Id == 704, "TMDb pagination results must be preserved");

var actorFilmography = RecommendationSelector.SelectFilmography(
    new TmdbPersonDetails
    {
        Id = 36662,
        Name = "Carey Mulligan",
        KnownForDepartment = "Acting",
        MovieCredits = new TmdbMovieCredits
        {
            Cast =
            [
                new TmdbMovieCredit { Id = 1000, Title = "Local movie", ReleaseDate = "2025-01-01", Character = "Lead" },
                new TmdbMovieCredit { Id = 1001, Title = "Newest external", ReleaseDate = "2024-02-02", Character = "Jane", PosterPath = "/person-movie.jpg" },
                new TmdbMovieCredit { Id = 1001, Title = "Duplicate credit", ReleaseDate = "2024-02-02", Character = "Jane" },
                new TmdbMovieCredit { Id = 1002, Title = "Older external", ReleaseDate = "2020-03-03", Character = "Mary" }
            ],
            Crew = [new TmdbMovieCredit { Id = 2000, Title = "Unrelated crew credit", ReleaseDate = "2023-01-01", Job = "Director" }]
        }
    },
    "Кэри Маллиган",
    new HashSet<string>(StringComparer.Ordinal) { "1000" },
    limit: 10);
Assert(actorFilmography.Heading == "Movies with Кэри Маллиган", "actor filmography must use the local person display name");
Assert(actorFilmography.Items.Count == 2, "actor filmography must remove local and duplicate movies");
Assert(actorFilmography.Items[0].SourceId == 1001 && actorFilmography.Items[0].Genre == "as Jane", "actor movies must be newest first and preserve the character");
Assert(actorFilmography.Items.All(item => item.MediaType == "Movie" && item.MatchPercent == 0), "filmography cards must be movies without invented match scores");

var directorFilmography = RecommendationSelector.SelectFilmography(
    new TmdbPersonDetails
    {
        Id = 525,
        Name = "Christopher Nolan",
        KnownForDepartment = "Directing",
        MovieCredits = new TmdbMovieCredits
        {
            Cast = [new TmdbMovieCredit { Id = 3000, Title = "Acting cameo", ReleaseDate = "2024-01-01" }],
            Crew =
            [
                new TmdbMovieCredit { Id = 3001, Title = "Directed movie", ReleaseDate = "2023-07-21", Job = "Director" },
                new TmdbMovieCredit { Id = 3002, Title = "Produced only", ReleaseDate = "2022-01-01", Job = "Producer" }
            ]
        }
    },
    "Christopher Nolan",
    new HashSet<string>(StringComparer.Ordinal),
    limit: 10);
Assert(directorFilmography.Heading == "Directed by Christopher Nolan", "director filmography must use a director-specific heading");
Assert(directorFilmography.Items.Count == 1 && directorFilmography.Items[0].SourceId == 3001, "director filmography must include only directing credits");
Assert(directorFilmography.Items[0].Genre == "Director", "director cards must identify the credit type");

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
Assert(details.TrailerProxyUrl is null, "the pure metadata selector must not issue a signed trailer URL");

var trailerTime = new MutableTimeProvider(new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero));
var trailerTokens = new TrailerTokenService(Enumerable.Repeat((byte)42, 32).ToArray(), trailerTime);
var trailerPath = trailerTokens.CreatePath("8g18jFHCLXk")
    ?? throw new InvalidOperationException("valid YouTube identifiers must receive a signed trailer path");
var trailerUri = new Uri($"https://media.kunini.ru{trailerPath}");
var trailerQuery = trailerUri.Query.TrimStart('?')
    .Split('&', StringSplitOptions.RemoveEmptyEntries)
    .Select(part => part.Split('=', 2))
    .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
Assert(trailerUri.AbsolutePath == "/KuniniDiscover/trailer/8g18jFHCLXk", "signed trailer paths must stay on the Jellyfin origin");
Assert(trailerTokens.IsValid(
    "8g18jFHCLXk",
    long.Parse(trailerQuery["expires"], System.Globalization.CultureInfo.InvariantCulture),
    trailerQuery["signature"]), "fresh signed trailer paths must validate");
Assert(!trailerTokens.IsValid(
    "Different01",
    long.Parse(trailerQuery["expires"], System.Globalization.CultureInfo.InvariantCulture),
    trailerQuery["signature"]), "a trailer signature must be bound to one video identifier");
trailerTime.Advance(TimeSpan.FromMinutes(16));
Assert(!trailerTokens.IsValid(
    "8g18jFHCLXk",
    long.Parse(trailerQuery["expires"], System.Globalization.CultureInfo.InvariantCulture),
    trailerQuery["signature"]), "expired trailer paths must fail closed");
Assert(trailerTokens.CreatePath("../bad-key") is null, "invalid YouTube identifiers must not receive signed paths");

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

Console.WriteLine("Kunini Discover tests passed: paged recommendations, search, person filmography, signed proxy trailers, metadata, embedded assets");

sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
