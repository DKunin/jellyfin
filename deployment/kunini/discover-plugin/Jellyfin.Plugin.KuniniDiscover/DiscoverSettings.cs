// SPDX-License-Identifier: GPL-2.0-only

namespace Jellyfin.Plugin.KuniniDiscover;

internal static class DiscoverSettings
{
    // Jellyfin's bundled TMDb provider key is public in the GPL source and can be
    // overridden for this plugin without changing the deployment artifact.
    private const string DefaultTmdbApiKey = "4219e299c89411838049ab0dab19ebd5";

    public static string TmdbApiKey
        => Environment.GetEnvironmentVariable("KUNINI_DISCOVER_TMDB_API_KEY") is { Length: > 0 } value
            ? value
            : DefaultTmdbApiKey;
}
