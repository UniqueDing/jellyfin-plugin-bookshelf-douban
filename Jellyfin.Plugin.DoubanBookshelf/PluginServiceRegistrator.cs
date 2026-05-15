using Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DoubanBookshelf
{
    /// <summary>
    /// Register Douban Bookshelf services.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<DoubanBookParser>();
            serviceCollection.AddSingleton<DoubanClient>();
        }
    }
}
