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
        serviceCollection.AddHttpClient<TmdbPosterClient>(client =>
        {
            client.BaseAddress = new Uri("https://i0.wp.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin.Plugin.KuniniDiscover/1.0");
        });
        serviceCollection.AddHttpClient<TrailerProxyClient>(client =>
        {
            var configuredUrl = Environment.GetEnvironmentVariable("KUNINI_TRAILER_PROXY_URL");
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(configuredUrl) ? "http://trailer-proxy:8080/" : configuredUrl,
                UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        serviceCollection.AddSingleton<TrailerTokenService>();
        serviceCollection.AddTransient<RecommendationService>();
    }
}
