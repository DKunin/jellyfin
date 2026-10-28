// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// A Kunini identity verified by the reverse proxy.
/// </summary>
/// <param name="UserId">Stable Kunini user id.</param>
/// <param name="Username">Normalized Kunini username.</param>
public sealed partial record KuniniIdentity(Guid UserId, string Username)
{
    /// <summary>
    /// Gets the isolated Jellyfin username for this identity.
    /// </summary>
    public string JellyfinUsername => $"kunini-{Username}-{UserId:N}";

    /// <summary>
    /// Parses a single trusted identity from proxy headers.
    /// </summary>
    /// <param name="headers">Incoming request headers.</param>
    /// <param name="identity">Parsed identity when successful.</param>
    /// <returns><see langword="true"/> only when both headers are present exactly once and valid.</returns>
    public static bool TryRead(IHeaderDictionary headers, out KuniniIdentity? identity)
    {
        identity = null;
        if (!headers.TryGetValue(Constants.UserHeader, out var usernames)
            || usernames.Count != 1
            || !headers.TryGetValue(Constants.UserIdHeader, out var userIds)
            || userIds.Count != 1)
        {
            return false;
        }

        var username = usernames[0];
        var userId = userIds[0];
        if (string.IsNullOrEmpty(username)
            || !UsernamePattern().IsMatch(username)
            || string.IsNullOrEmpty(userId)
            || !Guid.TryParse(userId, out var parsedUserId))
        {
            return false;
        }

        identity = new KuniniIdentity(parsedUserId, username);
        return true;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
