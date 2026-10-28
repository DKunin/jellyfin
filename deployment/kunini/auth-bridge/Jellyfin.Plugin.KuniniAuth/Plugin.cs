// SPDX-License-Identifier: GPL-2.0-only

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.KuniniAuth;

/// <summary>
/// Kunini Auth bridge plugin.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Kunini Auth Bridge";

    /// <inheritdoc />
    public override string Description => "Maps identities verified by auth.kunini.ru to isolated Jellyfin sessions.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("ff1ec3c4-2713-4ec2-a06b-c813351b5dfa");
}
