// SPDX-License-Identifier: GPL-2.0-only

using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// A response containing recommendations from one external catalog.
/// </summary>
/// <param name="Source">The external source name.</param>
/// <param name="Items">Recommendations absent from the local library.</param>
public sealed record RecommendationResponse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("items")] IReadOnlyList<RecommendationItem> Items);

/// <summary>
/// A recommended movie or series that is absent from the local library.
/// </summary>
/// <param name="SourceId">The item identifier at the external source.</param>
/// <param name="MediaType">Movie or Series.</param>
/// <param name="Title">The display title.</param>
/// <param name="Year">The release or premiere year.</param>
/// <param name="PosterUrl">The poster image URL, when available.</param>
/// <param name="Overview">The item overview.</param>
/// <param name="Genre">The primary genre.</param>
/// <param name="MatchPercent">A rank-derived recommendation strength.</param>
public sealed record RecommendationItem(
    [property: JsonPropertyName("sourceId")] int SourceId,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("posterUrl")] string? PosterUrl,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("genre")] string Genre,
    [property: JsonPropertyName("matchPercent")] int MatchPercent);

/// <summary>
/// Full metadata for a recommended movie or series that is not in Jellyfin.
/// </summary>
/// <param name="SourceId">The TMDb identifier.</param>
/// <param name="MediaType">Movie or Series.</param>
/// <param name="Title">The display title.</param>
/// <param name="OriginalTitle">The original-language title.</param>
/// <param name="Year">The release or premiere year.</param>
/// <param name="ReleaseDate">The release or premiere date.</param>
/// <param name="RuntimeMinutes">Movie or typical episode runtime.</param>
/// <param name="SeasonCount">The number of seasons for series.</param>
/// <param name="Certification">The content rating when available.</param>
/// <param name="Rating">The TMDb user rating.</param>
/// <param name="VoteCount">The TMDb rating vote count.</param>
/// <param name="Tagline">The title tagline.</param>
/// <param name="Overview">The synopsis.</param>
/// <param name="Genres">The ordered genre names.</param>
/// <param name="Status">The release status.</param>
/// <param name="PosterUrl">The same-origin poster URL.</param>
/// <param name="BackdropUrl">The same-origin backdrop URL.</param>
public sealed record ExternalDetailResponse(
    [property: JsonPropertyName("sourceId")] int SourceId,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("originalTitle")] string? OriginalTitle,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("releaseDate")] string? ReleaseDate,
    [property: JsonPropertyName("runtimeMinutes")] int? RuntimeMinutes,
    [property: JsonPropertyName("seasonCount")] int? SeasonCount,
    [property: JsonPropertyName("certification")] string? Certification,
    [property: JsonPropertyName("rating")] double Rating,
    [property: JsonPropertyName("voteCount")] int VoteCount,
    [property: JsonPropertyName("tagline")] string? Tagline,
    [property: JsonPropertyName("overview")] string Overview,
    [property: JsonPropertyName("genres")] IReadOnlyList<string> Genres,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("posterUrl")] string? PosterUrl,
    [property: JsonPropertyName("backdropUrl")] string? BackdropUrl);

internal enum RecommendationMediaKind
{
    Movie,
    Series
}
internal sealed class TmdbPage
{
    [JsonPropertyName("results")]
    public IReadOnlyList<TmdbCandidate> Results { get; init; } = [];
}

internal sealed class TmdbCandidate
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? MovieTitle { get; init; }

    [JsonPropertyName("name")]
    public string? SeriesTitle { get; init; }

    [JsonPropertyName("release_date")]
    public string? MovieReleaseDate { get; init; }

    [JsonPropertyName("first_air_date")]
    public string? SeriesReleaseDate { get; init; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("genre_ids")]
    public IReadOnlyList<int> GenreIds { get; init; } = [];
}

internal sealed class TmdbDetails
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? MovieTitle { get; init; }

    [JsonPropertyName("name")]
    public string? SeriesTitle { get; init; }

    [JsonPropertyName("original_title")]
    public string? OriginalMovieTitle { get; init; }

    [JsonPropertyName("original_name")]
    public string? OriginalSeriesTitle { get; init; }

    [JsonPropertyName("release_date")]
    public string? MovieReleaseDate { get; init; }

    [JsonPropertyName("first_air_date")]
    public string? SeriesReleaseDate { get; init; }

    [JsonPropertyName("runtime")]
    public int? MovieRuntimeMinutes { get; init; }

    [JsonPropertyName("episode_run_time")]
    public IReadOnlyList<int> EpisodeRuntimeMinutes { get; init; } = [];

    [JsonPropertyName("last_episode_to_air")]
    public TmdbEpisode? LastEpisodeToAir { get; init; }

    [JsonPropertyName("number_of_seasons")]
    public int? SeasonCount { get; init; }

    [JsonPropertyName("vote_average")]
    public double Rating { get; init; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; init; }

    [JsonPropertyName("tagline")]
    public string? Tagline { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; init; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; init; }

    [JsonPropertyName("genres")]
    public IReadOnlyList<TmdbGenre> Genres { get; init; } = [];

    [JsonPropertyName("release_dates")]
    public TmdbReleaseDateCollection? MovieReleaseDates { get; init; }

    [JsonPropertyName("content_ratings")]
    public TmdbContentRatingCollection? SeriesContentRatings { get; init; }
}

internal sealed class TmdbGenre
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class TmdbEpisode
{
    [JsonPropertyName("runtime")]
    public int? RuntimeMinutes { get; init; }
}

internal sealed class TmdbReleaseDateCollection
{
    [JsonPropertyName("results")]
    public IReadOnlyList<TmdbReleaseCountry> Results { get; init; } = [];
}

internal sealed class TmdbReleaseCountry
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("release_dates")]
    public IReadOnlyList<TmdbReleaseDateEntry> ReleaseDates { get; init; } = [];
}

internal sealed class TmdbReleaseDateEntry
{
    [JsonPropertyName("certification")]
    public string? Certification { get; init; }
}

internal sealed class TmdbContentRatingCollection
{
    [JsonPropertyName("results")]
    public IReadOnlyList<TmdbContentRating> Results { get; init; } = [];
}

internal sealed class TmdbContentRating
{
    [JsonPropertyName("iso_3166_1")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("rating")]
    public string? Rating { get; init; }
}
