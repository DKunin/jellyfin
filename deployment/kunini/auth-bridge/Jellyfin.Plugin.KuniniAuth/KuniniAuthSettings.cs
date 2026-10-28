// SPDX-License-Identifier: GPL-2.0-only

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Reads the deployment-controlled bridge settings.
/// </summary>
public static class KuniniAuthSettings
{
    private const string EnabledVariable = "KUNINI_AUTH_ENABLED";
    private const string AdminUsersVariable = "KUNINI_AUTH_ADMIN_USERS";
    private const string RecoveryAddressesVariable = "KUNINI_AUTH_RECOVERY_ADDRESSES";

    /// <summary>
    /// Gets a value indicating whether the provider is explicitly enabled.
    /// </summary>
    public static bool Enabled => string.Equals(
        Environment.GetEnvironmentVariable(EnabledVariable),
        "true",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether a verified Kunini username may receive Jellyfin administrator rights.
    /// </summary>
    /// <param name="username">The verified Kunini username.</param>
    /// <returns><see langword="true"/> when the username is explicitly allowlisted.</returns>
    public static bool IsAdministrator(string username)
    {
        var value = Environment.GetEnvironmentVariable(AdminUsersVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(username, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an address is an explicitly allowed source for local recovery requests.
    /// </summary>
    /// <param name="address">Observed connection source.</param>
    /// <returns><see langword="true"/> for loopback or an explicit deployment allowlist entry.</returns>
    public static bool IsRecoveryAddress(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address))
        {
            return true;
        }

        var value = Environment.GetEnvironmentVariable(RecoveryAddressesVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => System.Net.IPAddress.TryParse(candidate, out var parsed) && parsed.Equals(address));
    }
}
