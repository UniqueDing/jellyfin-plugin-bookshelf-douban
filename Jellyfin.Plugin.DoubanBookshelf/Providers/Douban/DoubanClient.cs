using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Client for Douban Books pages.
/// </summary>
public class DoubanClient
{
    private const int SearchResultLimit = 5;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DoubanClient> _logger;
    private readonly DoubanBookParser _parser;
    private readonly object _cookiesLock = new();
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubanClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DoubanClient}"/> interface.</param>
    /// <param name="parser">The Douban Books parser.</param>
    public DoubanClient(IHttpClientFactory httpClientFactory, ILogger<DoubanClient> logger, DoubanBookParser parser)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _parser = parser;
    }

    /// <summary>
    /// Search Douban Books.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed book results.</returns>
    public async Task<IReadOnlyList<DoubanBook>> SearchBooks(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = string.Format(CultureInfo.InvariantCulture, DoubanUrls.SearchUrl, WebUtility.UrlEncode(query));
        var html = await GetString(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html) || IsAccessBlocked(html))
        {
            return [];
        }

        var bookUrls = _parser.ParseSearchResults(html, SearchResultLimit);
        var books = new List<DoubanBook>();
        foreach (var bookUrl in bookUrls)
        {
            var book = await GetBookByUrl(bookUrl, cancellationToken).ConfigureAwait(false);
            if (book is not null)
            {
                books.Add(book);
            }
        }

        return books;
    }

    /// <summary>
    /// Get a Douban book by subject id.
    /// </summary>
    /// <param name="doubanId">The Douban subject id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed book.</returns>
    public Task<DoubanBook?> GetBookById(string doubanId, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, DoubanUrls.BookUrl, doubanId);
        return GetBookByUrl(url, cancellationToken);
    }

    /// <summary>
    /// Get an image response.
    /// </summary>
    /// <param name="url">The image url.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP response.</returns>
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = CreateRequest(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(DoubanUrls.BaseUrl);
        AddSessionCookies(request);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        StoreSessionCookies(response);
        return response;
    }

    private async Task<DoubanBook?> GetBookByUrl(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var html = await GetString(url, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(html) || IsAccessBlocked(html))
        {
            return null;
        }

        return _parser.ParseBook(url, html);
    }

    private async Task<string?> GetString(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = CreateRequest(HttpMethod.Get, url);
        AddSessionCookies(request);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        StoreSessionCookies(response);
        if (IsSecurityChallenge(response))
        {
            LogSecurityChallenge(url, response);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Douban request failed: {Url} ({StatusCode})", url, response.StatusCode);
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (IsAccessBlocked(html))
        {
            _logger.LogWarning("Douban blocked request or returned a security challenge page: {Url}", url);
            return null;
        }

        return html;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("DNT", "1");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        return request;
    }

    private void AddSessionCookies(HttpRequestMessage request)
    {
        lock (_cookiesLock)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }
        }
    }

    private void StoreSessionCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        lock (_cookiesLock)
        {
            foreach (var value in values)
            {
                var cookie = value.Split(';', 2)[0];
                var parts = cookie.Split('=', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    _cookies[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
    }

    private static bool IsSecurityChallenge(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        var location = response.Headers.Location;
        if (location is not null && string.Equals(location.Host, "sec.douban.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(response.RequestMessage?.RequestUri?.Host, "sec.douban.com", StringComparison.OrdinalIgnoreCase);
    }

    private void LogSecurityChallenge(string url, HttpResponseMessage response)
    {
        var challengeUrl = response.Headers.Location ?? response.RequestMessage?.RequestUri;
        _logger.LogWarning(
            "Douban blocked request or returned a security challenge: {Url} ({StatusCode}) ChallengeUrl={ChallengeUrl}",
            url,
            response.StatusCode,
            challengeUrl);
    }

    private static bool IsAccessBlocked(string html)
    {
        return html.Contains("<title>禁止访问</title>", StringComparison.Ordinal)
            || html.Contains("<title>豆瓣 - 登录跳转页</title>", StringComparison.Ordinal)
            || html.Contains("sec.douban.com", StringComparison.OrdinalIgnoreCase)
            || html.Contains("检测到有异常请求", StringComparison.Ordinal)
            || html.Contains("captcha", StringComparison.OrdinalIgnoreCase);
    }
}
