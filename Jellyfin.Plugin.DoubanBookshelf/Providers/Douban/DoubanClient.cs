using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Plugin.DoubanBookshelf.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;

/// <summary>
/// Client for Douban Books pages.
/// </summary>
public sealed class DoubanClient : IDisposable
{
    private const int SearchResultLimit = 5;
    private const int GuestRequestDelayMilliseconds = 5000;
    private const int LoggedInRequestDelayMilliseconds = 3000;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DoubanClient> _logger;
    private readonly DoubanBookParser _parser;
    private readonly Func<PluginConfiguration?> _configurationProvider;
    private readonly object _cookiesLock = new();
    private readonly SemaphoreSlim _requestThrottle = new(1, 1);
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _configuredCookieNames = new(StringComparer.Ordinal);
    private string _configuredCookies = string.Empty;
    private DateTimeOffset _lastRiskControlledRequest = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="DoubanClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DoubanClient}"/> interface.</param>
    /// <param name="parser">The Douban Books parser.</param>
    public DoubanClient(IHttpClientFactory httpClientFactory, ILogger<DoubanClient> logger, DoubanBookParser parser)
        : this(httpClientFactory, logger, parser, () => Plugin.Instance?.Configuration)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += (_, _) => LoadConfiguredCookies();
        }
    }

    internal DoubanClient(IHttpClientFactory httpClientFactory, ILogger<DoubanClient> logger, DoubanBookParser parser, Func<PluginConfiguration?> configurationProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _parser = parser;
        _configurationProvider = configurationProvider;
        LoadConfiguredCookies();
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

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _requestThrottle.Dispose();
        }
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
        await WaitForRiskControlledRequest(cancellationToken).ConfigureAwait(false);

        var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = CreateRequest(HttpMethod.Get, url);
        AddSessionCookies(request);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        StoreSessionCookies(response);
        if (IsSecurityChallenge(response))
        {
            if (IsDoubanAvoidRiskControlEnabled())
            {
                var retryHtml = await TrySolveSecurityChallengeAndRetry(httpClient, request, response, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(retryHtml))
                {
                    return retryHtml;
                }
            }

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
            if (IsDoubanAvoidRiskControlEnabled())
            {
                var retryHtml = await TrySolveSecurityChallengeAndRetry(httpClient, request, response, html, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(retryHtml))
                {
                    return retryHtml;
                }
            }

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

    private async Task<string?> TrySolveSecurityChallengeAndRetry(HttpClient httpClient, HttpRequestMessage originalRequest, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return await TrySolveSecurityChallengeAndRetry(httpClient, originalRequest, response, html, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TrySolveSecurityChallengeAndRetry(HttpClient httpClient, HttpRequestMessage originalRequest, HttpResponseMessage response, string html, CancellationToken cancellationToken)
    {
        if (!TryGetSecurityChallenge(response, html, out var challengeUri, out var token, out var challenge, out var difficulty))
        {
            return null;
        }

        try
        {
            var solution = await SolveNonce(challenge, difficulty, cancellationToken).ConfigureAwait(false);
            using var validationRequest = CreateRequest(HttpMethod.Post, challengeUri.ToString());
            validationRequest.Headers.Referrer = response.RequestMessage?.RequestUri ?? originalRequest.RequestUri;
            validationRequest.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("tok", token),
                new KeyValuePair<string, string>("cha", challenge),
                new KeyValuePair<string, string>("sol", solution.ToString(CultureInfo.InvariantCulture))
            ]);
            AddSessionCookies(validationRequest);

            using var validationResponse = await httpClient.SendAsync(validationRequest, cancellationToken).ConfigureAwait(false);
            StoreSessionCookies(validationResponse);

            await WaitForRiskControlledRequest(cancellationToken).ConfigureAwait(false);

            using var retryRequest = CreateRequest(originalRequest.Method, originalRequest.RequestUri?.ToString() ?? string.Empty);
            AddSessionCookies(retryRequest);
            using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            StoreSessionCookies(retryResponse);
            if (!retryResponse.IsSuccessStatusCode || IsSecurityChallenge(retryResponse))
            {
                return null;
            }

            var retryHtml = await retryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return IsAccessBlocked(retryHtml) ? null : retryHtml;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to solve Douban security challenge: {Url}", originalRequest.RequestUri);
            return null;
        }
    }

    private async Task WaitForRiskControlledRequest(CancellationToken cancellationToken)
    {
        if (!IsDoubanAvoidRiskControlEnabled())
        {
            return;
        }

        await _requestThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = HasConfiguredCookies() ? LoggedInRequestDelayMilliseconds : GuestRequestDelayMilliseconds;
            var elapsed = DateTimeOffset.UtcNow - _lastRiskControlledRequest;
            var remaining = TimeSpan.FromMilliseconds(delay) - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }

            _lastRiskControlledRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _requestThrottle.Release();
        }
    }

    private bool IsDoubanAvoidRiskControlEnabled()
    {
        return _configurationProvider()?.EnableDoubanAvoidRiskControl ?? false;
    }

    private bool HasConfiguredCookies()
    {
        lock (_cookiesLock)
        {
            return !string.IsNullOrWhiteSpace(_configuredCookies);
        }
    }

    private void LoadConfiguredCookies()
    {
        var configuredCookies = _configurationProvider()?.DoubanCookies ?? string.Empty;
        lock (_cookiesLock)
        {
            foreach (var cookieName in _configuredCookieNames)
            {
                _cookies.Remove(cookieName);
            }

            _configuredCookieNames.Clear();
            _configuredCookies = configuredCookies;
            foreach (var cookie in ParseConfiguredCookies(configuredCookies))
            {
                _cookies[cookie.Key] = cookie.Value;
                _configuredCookieNames.Add(cookie.Key);
            }
        }
    }

    internal static IReadOnlyDictionary<string, string> ParseConfiguredCookies(string? cookies)
    {
        var parsedCookies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(cookies))
        {
            return parsedCookies;
        }

        foreach (var cookie in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = cookie.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = cookie[..separatorIndex].Trim();
            var value = cookie[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                parsedCookies[name] = value;
            }
        }

        return parsedCookies;
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

    private static bool TryGetSecurityChallenge(HttpResponseMessage response, string html, out Uri challengeUri, out string token, out string challenge, out int difficulty)
    {
        challengeUri = response.Headers.Location ?? response.RequestMessage?.RequestUri ?? new Uri(DoubanUrls.BaseUrl);
        token = string.Empty;
        challenge = string.Empty;
        difficulty = 4;

        if (!string.Equals(challengeUri.Host, "sec.douban.com", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("sec.douban.com", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("name=\"cha\"", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("id=\"cha\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        token = GetInputValue(document, "tok");
        challenge = GetInputValue(document, "cha");
        var difficultyText = GetInputValue(document, "difficulty");
        if (int.TryParse(difficultyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDifficulty))
        {
            difficulty = parsedDifficulty;
        }

        var formAction = document.DocumentNode.SelectSingleNode("//form")?.GetAttributeValue("action", string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(formAction))
        {
            challengeUri = Uri.TryCreate(formAction, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(new Uri("https://sec.douban.com"), formAction);
        }

        return !string.IsNullOrWhiteSpace(challenge);
    }

    private static string GetInputValue(HtmlDocument document, string name)
    {
        return document.DocumentNode.SelectSingleNode($"//input[@id='{name}']")?.GetAttributeValue("value", string.Empty)
            ?? document.DocumentNode.SelectSingleNode($"//input[@name='{name}']")?.GetAttributeValue("value", string.Empty)
            ?? string.Empty;
    }

    private static async Task<long> SolveNonce(string challenge, int difficulty, CancellationToken cancellationToken)
    {
        var targetPrefix = new string('0', Math.Max(0, difficulty));
        long nonce = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nonce++;
            var hash = ComputeSha512Hex(challenge + nonce.ToString(CultureInfo.InvariantCulture));
            if (hash.StartsWith(targetPrefix, StringComparison.Ordinal))
            {
                return nonce;
            }

            if ((nonce & 0xFFF) == 0)
            {
                await Task.Yield();
            }
        }
    }

    private static string ComputeSha512Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA512.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
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
