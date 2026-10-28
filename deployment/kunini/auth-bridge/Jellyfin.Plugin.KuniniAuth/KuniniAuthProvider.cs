// SPDX-License-Identifier: GPL-2.0-only

using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Authenticates a synthetic login as the identity verified by Kunini Auth and Nginx.
/// </summary>
public sealed class KuniniAuthProvider : IAuthenticationProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApplicationHost _applicationHost;
    private readonly ILogger<KuniniAuthProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KuniniAuthProvider"/> class.
    /// </summary>
    public KuniniAuthProvider(
        IHttpContextAccessor httpContextAccessor,
        IApplicationHost applicationHost,
        ILogger<KuniniAuthProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _applicationHost = applicationHost;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Kunini Auth";

    /// <inheritdoc />
    public bool IsEnabled => KuniniAuthSettings.Enabled;

    /// <inheritdoc />
    public async Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        if (!IsEnabled || !string.Equals(username, Constants.LoginUsername, StringComparison.Ordinal))
        {
            throw new AuthenticationException("Invalid username or password");
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null || !KuniniIdentity.TryRead(request.Headers, out var identity) || identity is null)
        {
            _logger.LogWarning("Kunini authentication was rejected because trusted identity headers were missing or invalid");
            throw new AuthenticationException("Trusted Kunini identity is unavailable");
        }

        var jellyfinUsername = identity.JellyfinUsername;
        var userManager = _applicationHost.Resolve<IUserManager>();
        var user = userManager.GetUserByName(jellyfinUsername);
        if (user is null)
        {
            _logger.LogInformation("Creating Jellyfin account for verified Kunini user {KuniniUsername}", identity.Username);
            user = await userManager.CreateUserAsync(jellyfinUsername).ConfigureAwait(false);
            user.SetPermission(PermissionKind.IsAdministrator, KuniniAuthSettings.IsAdministrator(identity.Username));
            user.SetPermission(PermissionKind.EnableAllFolders, true);
            user.SetPermission(PermissionKind.EnableMediaPlayback, true);
            user.AuthenticationProviderId = GetType().FullName!;
            await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        }

        return new ProviderAuthenticationResult
        {
            Username = jellyfinUsername
        };
    }

    /// <inheritdoc />
    public Task ChangePassword(User user, string newPassword)
    {
        throw new InvalidOperationException("Kunini-managed Jellyfin accounts do not support local passwords");
    }
}
