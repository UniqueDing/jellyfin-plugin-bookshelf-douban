namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Douban Books urls.
/// </summary>
public static class DoubanUrls
{
    /// <summary>
    /// Gets the Douban Books base url.
    /// </summary>
    public const string BaseUrl = "https://book.douban.com/";

    /// <summary>
    /// Gets the search url.
    /// </summary>
    public const string SearchUrl = "https://www.douban.com/search?cat=1001&q={0}";

    /// <summary>
    /// Gets the book detail url.
    /// </summary>
    public const string BookUrl = "https://book.douban.com/subject/{0}/";
}
