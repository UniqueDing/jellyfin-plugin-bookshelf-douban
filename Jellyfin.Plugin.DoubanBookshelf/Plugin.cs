using System;
using Jellyfin.Plugin.DoubanBookshelf.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DoubanBookshelf
{
    /// <summary>
    /// Plugin entrypoint.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
        }

        /// <inheritdoc />
        public override string Name => "Douban Bookshelf";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("d7072285-d6f1-442f-8473-f7c2e76446d8");
    }
}
