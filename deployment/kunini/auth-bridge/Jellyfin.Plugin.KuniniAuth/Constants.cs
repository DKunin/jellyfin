// SPDX-License-Identifier: GPL-2.0-only

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Constants shared by the Kunini authentication bridge.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Gets the synthetic Jellyfin username used to invoke the provider.
    /// </summary>
    public const string LoginUsername = "KuniniAuth";

    /// <summary>
    /// Gets the reverse-proxy header containing the verified Kunini username.
    /// </summary>
    public const string UserHeader = "X-Kunini-Auth-User";

    /// <summary>
    /// Gets the reverse-proxy header containing the stable Kunini user id.
    /// </summary>
    public const string UserIdHeader = "X-Kunini-Auth-User-Id";

    /// <summary>
    /// Gets the reverse-proxy header marking the dedicated direct-access endpoint.
    /// </summary>
    public const string DirectAccessHeader = "X-Kunini-Direct-Access";
}
