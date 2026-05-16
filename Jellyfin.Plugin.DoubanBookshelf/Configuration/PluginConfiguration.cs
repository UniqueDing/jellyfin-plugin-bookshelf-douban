using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DoubanBookshelf.Configuration
{
    /// <summary>
    /// Douban Bookshelf plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether Douban anti-blocking request pacing is enabled.
        /// </summary>
        public bool EnableDoubanAvoidRiskControl { get; set; }

        /// <summary>
        /// Gets or sets manually configured Douban cookies.
        /// </summary>
        public string DoubanCookies { get; set; } = string.Empty;
    }
}
