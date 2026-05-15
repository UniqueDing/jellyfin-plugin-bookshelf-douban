using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Parses Douban Books HTML pages.
/// </summary>
public partial class DoubanBookParser
{
    /// <summary>
    /// Parse Douban search result HTML into book detail urls.
    /// </summary>
    /// <param name="html">The search result HTML.</param>
    /// <param name="maxResults">The maximum number of urls to return.</param>
    /// <returns>The book detail urls.</returns>
    public IReadOnlyList<string> ParseSearchResults(string html, int maxResults)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var results = new List<string>();
        var linkNodes = document.DocumentNode.SelectNodes("//a[contains(concat(' ', normalize-space(@class), ' '), ' nbg ')]");
        if (linkNodes is null)
        {
            return results;
        }

        foreach (var node in linkNodes)
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            var url = ResolveSearchResultUrl(node.GetAttributeValue("href", string.Empty));
            if (!string.IsNullOrWhiteSpace(url) && !results.Contains(url, StringComparer.Ordinal))
            {
                results.Add(url);
            }
        }

        return results;
    }

    /// <summary>
    /// Parse Douban book detail HTML into metadata.
    /// </summary>
    /// <param name="fallbackUrl">The url used to load the page.</param>
    /// <param name="html">The book detail HTML.</param>
    /// <returns>The parsed book metadata.</returns>
    public DoubanBook? ParseBook(string fallbackUrl, string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var title = GetText(document.DocumentNode.SelectSingleNode("//span[@property='v:itemreviewed']"));
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var url = document.DocumentNode.SelectSingleNode("//a[@data-url]")?.GetAttributeValue("data-url", string.Empty);
        if (string.IsNullOrWhiteSpace(url))
        {
            url = fallbackUrl;
        }

        var id = GetSubjectId(url);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        IEnumerable<HtmlNode> infoNodes = document.DocumentNode.SelectNodes("//span[contains(concat(' ', normalize-space(@class), ' '), ' pl ')]") ?? Enumerable.Empty<HtmlNode>();
        var authors = new List<string>();
        var translators = new List<string>();
        string? publisher = null;
        string? publishedDate = null;
        string? isbn = null;
        string? series = null;

        foreach (var node in infoNodes)
        {
            var label = GetText(node);
            var parent = node.ParentNode;
            if (parent is null)
            {
                continue;
            }

            if (label.StartsWith("作者", StringComparison.Ordinal))
            {
                authors.AddRange(GetPeople(node));
            }
            else if (label.StartsWith("译者", StringComparison.Ordinal))
            {
                translators.AddRange(GetPeople(node));
            }
            else if (label.StartsWith("出版社", StringComparison.Ordinal))
            {
                publisher = GetTailText(node);
            }
            else if (label.StartsWith("副标题", StringComparison.Ordinal))
            {
                var subtitle = GetTailText(node);
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    title = string.Concat(title, ":", subtitle);
                }
            }
            else if (label.StartsWith("出版年", StringComparison.Ordinal))
            {
                publishedDate = GetTailText(node);
            }
            else if (label.StartsWith("ISBN", StringComparison.Ordinal))
            {
                isbn = GetTailText(node);
            }
            else if (label.StartsWith("丛书", StringComparison.Ordinal))
            {
                series = GetNextElementText(node);
            }
        }

        return new DoubanBook
        {
            Id = id,
            Title = title,
            Url = url,
            CoverUrl = GetCoverUrl(document),
            Rating = GetRating(document),
            Authors = authors.Distinct(StringComparer.Ordinal).ToList(),
            Translators = translators.Distinct(StringComparer.Ordinal).ToList(),
            Publisher = publisher,
            PublishedDate = publishedDate,
            Isbn = isbn,
            Series = series,
            Description = GetDescription(document),
            Tags = GetTags(html),
            Language = GetBookLanguage(title)
        };
    }

    /// <summary>
    /// Try to extract the Douban subject id from a url.
    /// </summary>
    /// <param name="url">The Douban url.</param>
    /// <returns>The subject id.</returns>
    public string? GetSubjectId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = SubjectUrlRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ResolveSearchResultUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(href);
        if (SubjectUrlRegex().IsMatch(decoded))
        {
            return decoded;
        }

        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var queryValues = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var queryValue in queryValues)
        {
            var parts = queryValue.Split('=', 2);
            if (parts.Length != 2 || !parts[0].Equals("url", StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = Uri.UnescapeDataString(parts[1]);
            return SubjectUrlRegex().IsMatch(candidate) ? candidate : null;
        }

        return null;
    }

    private static List<string> GetPeople(HtmlNode labelNode)
    {
        var people = new List<string>();
        var next = labelNode.NextSibling;
        while (next is not null)
        {
            if (next.Name.Equals("span", StringComparison.OrdinalIgnoreCase)
                && next.GetAttributeValue("class", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("pl"))
            {
                break;
            }

            if (next.NodeType == HtmlNodeType.Element && next.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                var text = GetText(next);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    people.Add(text);
                }
            }

            next = next.NextSibling;
        }

        return people;
    }

    private static string? GetCoverUrl(HtmlDocument document)
    {
        var cover = document.DocumentNode
            .SelectSingleNode("//a[contains(concat(' ', normalize-space(@class), ' '), ' nbg ')]")
            ?.GetAttributeValue("href", string.Empty);
        if (string.IsNullOrWhiteSpace(cover) || cover.EndsWith("update_image", StringComparison.Ordinal))
        {
            return null;
        }

        return cover;
    }

    private static float? GetRating(HtmlDocument document)
    {
        var ratingText = GetText(document.DocumentNode.SelectSingleNode("//strong[@property='v:average']"));
        return float.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : null;
    }

    private static string? GetDescription(HtmlDocument document)
    {
        var introNodes = document.DocumentNode.SelectNodes("//div[@id='link-report']//div[contains(concat(' ', normalize-space(@class), ' '), ' intro ')]");
        var intro = introNodes?.LastOrDefault();
        return intro is null ? null : WebUtility.HtmlDecode(intro.InnerHtml.Trim());
    }

    private static List<string> GetTags(string html)
    {
        var match = TagsRegex().Match(html);
        if (!match.Success)
        {
            return [];
        }

        return match.Groups[1].Value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.StartsWith("7:", StringComparison.Ordinal))
            .Select(tag => tag[2..])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string GetBookLanguage(string title)
    {
        return title.Contains("英文版", StringComparison.Ordinal) || EnglishTitleRegex().IsMatch(title) ? "en" : "zh";
    }

    private static string GetText(HtmlNode? node)
    {
        return WebUtility.HtmlDecode(node?.InnerText ?? string.Empty).Trim();
    }

    private static string? GetTailText(HtmlNode node)
    {
        var text = string.Empty;
        var next = node.NextSibling;
        while (next is not null)
        {
            if (next.NodeType == HtmlNodeType.Text)
            {
                text += WebUtility.HtmlDecode(next.InnerText).Trim();
            }
            else if (next.NodeType == HtmlNodeType.Element)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = GetText(next);
                }

                break;
            }

            next = next.NextSibling;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? GetNextElementText(HtmlNode node)
    {
        var next = node.NextSibling;
        while (next is not null)
        {
            if (next.NodeType == HtmlNodeType.Element)
            {
                var text = GetText(next);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            next = next.NextSibling;
        }

        return null;
    }

    [GeneratedRegex(".*/subject/(\\d+)/?", RegexOptions.Compiled)]
    private static partial Regex SubjectUrlRegex();

    [GeneratedRegex("criteria = '(.+)'", RegexOptions.Compiled)]
    private static partial Regex TagsRegex();

    [GeneratedRegex("^[a-zA-Z\\-_]+$", RegexOptions.Compiled)]
    private static partial Regex EnglishTitleRegex();
}
