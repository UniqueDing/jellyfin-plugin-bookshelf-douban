using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <inheritdoc />
public class DoubanExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => DoubanConstants.ProviderName;

    /// <inheritdoc />
    public string Key => DoubanConstants.ProviderId;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Book;
    }
}
