// SPDX-License-Identifier: GPL-2.0-only

using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Registers Kunini Discover services.
/// </summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddMemoryCache();
        serviceCollection.AddHttpClient<TmdbRecommendationClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.themoviedb.org/3/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.KuniniDiscover/1.0");
        });
        serviceCollection.AddTransient<RecommendationService>();
    }
}
