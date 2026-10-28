// SPDX-License-Identifier: GPL-2.0-only

using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Registers the authentication provider.
/// </summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpContextAccessor();
        serviceCollection.AddSingleton<IAuthenticationProvider, KuniniAuthProvider>();
        serviceCollection.AddSingleton<IAuthorizationHandler, KuniniIdentityAuthorizationHandler>();
    }
}
