// SPDX-License-Identifier: GPL-2.0-only

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Rejects authenticated requests whose Jellyfin token belongs to another Kunini identity.
/// </summary>
public sealed class KuniniIdentityAuthorizationHandler : IAuthorizationHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<KuniniIdentityAuthorizationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KuniniIdentityAuthorizationHandler"/> class.
    /// </summary>
    public KuniniIdentityAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<KuniniIdentityAuthorizationHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (!KuniniAuthSettings.Enabled)
        {
            return Task.CompletedTask;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        var username = context.User.FindFirstValue(ClaimTypes.Name);
        if (request is null || !KuniniRequestBinding.IsAllowed(request, username))
        {
            _logger.LogWarning("Jellyfin authorization was rejected because the token did not match the verified Kunini identity");
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
