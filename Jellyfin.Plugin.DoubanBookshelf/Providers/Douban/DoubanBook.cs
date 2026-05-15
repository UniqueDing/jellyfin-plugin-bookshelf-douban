using System.Collections.Generic;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Parsed Douban book metadata.
/// </summary>
public class DoubanBook
{
    /// <summary>
    /// Gets or sets the Douban subject id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Douban detail url.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cover url.
    /// </summary>
    public string? CoverUrl { get; set; }

    /// <summary>
    /// Gets or sets the community rating on a 10 point scale.
    /// </summary>
    public float? Rating { get; set; }

    /// <summary>
    /// Gets the authors.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// Gets the translators.
    /// </summary>
    public IReadOnlyList<string> Translators { get; init; } = [];

    /// <summary>
    /// Gets or sets the publisher.
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Gets or sets the published date.
    /// </summary>
    public string? PublishedDate { get; set; }

    /// <summary>
    /// Gets or sets the ISBN.
    /// </summary>
    public string? Isbn { get; set; }

    /// <summary>
    /// Gets or sets the series.
    /// </summary>
    public string? Series { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets or sets the language.
    /// </summary>
    public string? Language { get; set; }
}
