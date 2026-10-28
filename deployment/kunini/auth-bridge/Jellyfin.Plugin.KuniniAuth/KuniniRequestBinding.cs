// SPDX-License-Identifier: GPL-2.0-only

using System.Net;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Validates that a Jellyfin token belongs to the identity on the current proxy request.
/// </summary>
public static class KuniniRequestBinding
{
    /// <summary>
    /// Checks the request identity binding, with a narrow loopback recovery exception.
    /// </summary>
    /// <param name="request">Current HTTP request.</param>
    /// <param name="authenticatedUsername">Username from the authenticated Jellyfin token.</param>
    /// <returns><see langword="true"/> when the request is bound to the expected user.</returns>
    public static bool IsAllowed(HttpRequest request, string? authenticatedUsername)
    {
        var hasUserHeader = request.Headers.ContainsKey(Constants.UserHeader);
        var hasUserIdHeader = request.Headers.ContainsKey(Constants.UserIdHeader);
        if (!hasUserHeader && !hasUserIdHeader)
        {
            return IsLoopbackRecoveryRequest(request);
        }

        if (!KuniniIdentity.TryRead(request.Headers, out var identity) || identity is null)
        {
            return false;
        }

        return !string.IsNullOrEmpty(authenticatedUsername)
               && string.Equals(authenticatedUsername, identity.JellyfinUsername, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackRecoveryRequest(HttpRequest request)
    {
        var remoteAddress = request.HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !KuniniAuthSettings.IsRecoveryAddress(remoteAddress))
        {
            return false;
        }

        return string.Equals(request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase)
               || IPAddress.TryParse(request.Host.Host, out var hostAddress) && IPAddress.IsLoopback(hostAddress);
    }
}
