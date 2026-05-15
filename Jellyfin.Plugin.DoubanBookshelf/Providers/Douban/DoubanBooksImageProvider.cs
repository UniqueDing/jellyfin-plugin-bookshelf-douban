using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Douban Books image provider.
/// </summary>
public class DoubanBooksImageProvider : IRemoteImageProvider
{
    private readonly DoubanClient _doubanClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubanBooksImageProvider"/> class.
    /// </summary>
    /// <param name="doubanClient">The Douban Books client.</param>
    public DoubanBooksImageProvider(DoubanClient doubanClient)
    {
        _doubanClient = doubanClient;
    }

    /// <inheritdoc />
    public string Name => DoubanConstants.ProviderName;

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is Book;
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        yield return ImageType.Primary;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var doubanId = item.GetProviderId(DoubanConstants.ProviderId);
        if (string.IsNullOrWhiteSpace(doubanId))
        {
            return [];
        }

        var book = await _doubanClient.GetBookById(doubanId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(book?.CoverUrl))
        {
            return [];
        }

        return [new RemoteImageInfo { ProviderName = Name, Url = book.CoverUrl }];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _doubanClient.GetImageResponse(url, cancellationToken);
    }
}
