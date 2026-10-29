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
/// <param name="DetailsUrl">The external metadata details URL.</param>
/// <param name="Overview">The item overview.</param>
/// <param name="Genre">The primary genre.</param>
/// <param name="MatchPercent">A rank-derived recommendation strength.</param>
public sealed record RecommendationItem(
    [property: JsonPropertyName("sourceId")] int SourceId,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("posterUrl")] string? PosterUrl,
    [property: JsonPropertyName("detailsUrl")] string DetailsUrl,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("genre")] string Genre,
    [property: JsonPropertyName("matchPercent")] int MatchPercent);

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
