// SPDX-License-Identifier: GPL-2.0-only

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.KuniniDiscover;

/// <summary>
/// Adds external TMDb recommendations and title search to Jellyfin Web.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="xmlSerializer">The Jellyfin XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Kunini Discover";

    /// <inheritdoc />
    public override string Description => "Shows TMDb recommendations and search results that are not already in the Jellyfin library.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("8f98fe96-76c8-44cf-9e88-d35021153ad8");
}
