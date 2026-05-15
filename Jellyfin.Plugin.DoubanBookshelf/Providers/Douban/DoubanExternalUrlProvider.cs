using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// External url provider for Douban Books.
/// </summary>
public class DoubanExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => DoubanConstants.ProviderName;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item.TryGetProviderId(DoubanConstants.ProviderId, out var externalId) && item is Book)
        {
            yield return string.Format(System.Globalization.CultureInfo.InvariantCulture, DoubanUrls.BookUrl, externalId);
        }
    }
}
