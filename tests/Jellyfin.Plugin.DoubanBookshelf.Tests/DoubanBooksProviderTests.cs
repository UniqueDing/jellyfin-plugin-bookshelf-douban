using System.Net;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;
using Jellyfin.Plugin.DoubanBookshelf.Tests.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jellyfin.Plugin.DoubanBookshelf.Tests;

public class DoubanBooksProviderTests
{
    private static DoubanBooksProvider CreateProvider(IHttpClientFactory httpClientFactory)
    {
        var doubanClient = new DoubanClient(httpClientFactory, NullLogger<DoubanClient>.Instance, new DoubanBookParser());
        return new DoubanBooksProvider(doubanClient);
    }

    private static bool HasProviderId(string id, Dictionary<string, string> providerIds)
    {
        return providerIds.TryGetValue(DoubanConstants.ProviderId, out var providerId) && providerId == id;
    }

    [Fact]
    public async Task GetSearchResults_ByIsbn_ReturnsDoubanResult()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("search?cat=1001", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-search.html"))),
            (uri => uri.AbsoluteUri.Contains("subject/26912767", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-detail.html")))
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var results = await provider.GetSearchResults(new BookInfo { ProviderIds = { { DoubanConstants.IsbnProviderId, "9787111544937" } } }, CancellationToken.None);

        Assert.Collection(
            results,
            first =>
            {
                Assert.Equal(DoubanConstants.ProviderName, first.SearchProviderName);
                Assert.Equal("深入理解计算机系统 (第3版):原书第3版", first.Name);
                Assert.Equal("https://img1.doubanio.com/view/subject/l/public/s29195878.jpg", first.ImageUrl);
                Assert.Equal(2016, first.ProductionYear);
                Assert.True(HasProviderId("26912767", first.ProviderIds));
            });
    }

    [Fact]
    public async Task GetMetadata_ByProviderId_ReturnsBookMetadata()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("subject/26912767", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-detail.html")))
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var metadata = await provider.GetMetadata(new BookInfo { ProviderIds = { { DoubanConstants.ProviderId, "26912767" } } }, CancellationToken.None);

        Assert.True(metadata.QueriedById);
        Assert.True(metadata.HasMetadata);
        Assert.Equal("zh", metadata.ResultLanguage);
        Assert.True(HasProviderId("26912767", metadata.Item.ProviderIds));
        Assert.Equal("深入理解计算机系统 (第3版):原书第3版", metadata.Item.Name);
        Assert.Equal("计算机科学丛书", metadata.Item.SeriesName);
        Assert.Equal(2016, metadata.Item.ProductionYear);
        Assert.Equal(9.7F, metadata.Item.CommunityRating);
        Assert.Collection(metadata.Item.Studios, studio => Assert.Equal("机械工业出版社", studio));
        Assert.Collection(metadata.Item.Genres, genre => Assert.Equal("计算机", genre));
        Assert.Collection(
            metadata.Item.Tags,
            first => Assert.Equal("计算机科学", first),
            second => Assert.Equal("CSAPP", second));
        Assert.Collection(
            metadata.People,
            first =>
            {
                Assert.Equal("Randal E. Bryant", first.Name);
                Assert.Equal(PersonKind.Author, first.Type);
            },
            second =>
            {
                Assert.Equal("David O'Hallaron", second.Name);
                Assert.Equal(PersonKind.Author, second.Type);
            },
            third =>
            {
                Assert.Equal("龚奕利", third.Name);
                Assert.Equal(PersonKind.Translator, third.Type);
            },
            fourth =>
            {
                Assert.Equal("贺莲", fourth.Name);
                Assert.Equal(PersonKind.Translator, fourth.Type);
            });
    }

    [Fact]
    public void GetSearchString_WithIsbnPrefersIsbn()
    {
        var provider = CreateProvider(Substitute.For<IHttpClientFactory>());

        var searchString = provider.GetSearchString(new BookInfo
        {
            Name = "深入理解计算机系统",
            ProviderIds = { { DoubanConstants.IsbnProviderId, "9787111544937" } }
        });

        Assert.Equal("9787111544937", searchString);
    }

    [Fact]
    public void GetSearchString_WithTrailingDoubanId_RemovesIdFromTitleQuery()
    {
        var provider = CreateProvider(Substitute.For<IHttpClientFactory>());

        var searchString = provider.GetSearchString(new BookInfo
        {
            Name = "深入理解计算机系统26912767"
        });

        Assert.Equal("深入理解计算机系统", searchString);
    }

    [Fact]
    public async Task GetSearchResults_WithTrailingDoubanId_UsesDoubanIdLookup()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("subject/26912767", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-detail.html")))
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var results = await provider.GetSearchResults(new BookInfo { Name = "深入理解计算机系统26912767" }, CancellationToken.None);

        Assert.Collection(
            results,
            first =>
            {
                Assert.Equal(DoubanConstants.ProviderName, first.SearchProviderName);
                Assert.Equal("深入理解计算机系统 (第3版):原书第3版", first.Name);
                Assert.True(HasProviderId("26912767", first.ProviderIds));
            });
    }

    [Fact]
    public async Task GetSearchResults_WhenDoubanReturnsForbidden_ReturnsNoResults()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("search?cat=1001", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.Forbidden, string.Empty))
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var results = await provider.GetSearchResults(new BookInfo { Name = "百年孤独" }, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSearchResults_WhenDoubanRedirectsToSecurityChallenge_ReturnsNoResults()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("search?cat=1001", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.Redirect, string.Empty)
            {
                Location = new Uri("https://sec.douban.com/b?r=https%3A%2F%2Fwww.douban.com%2Fsearch")
            })
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var results = await provider.GetSearchResults(new BookInfo { Name = "百年孤独" }, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSearchResults_SendsBrowserHeadersAndReusesCookies()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(
            new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
            {
                (uri => uri.AbsoluteUri.Contains("search?cat=1001", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-search.html"))
                {
                    SetCookies = ["bid=test-cookie; Expires=Fri, 14-May-27 13:21:11 GMT; Domain=.douban.com; Path=/"]
                }),
                (uri => uri.AbsoluteUri.Contains("subject/26912767", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-detail.html")))
            },
            request => requests.Add(request));
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        IRemoteMetadataProvider<Book, BookInfo> provider = CreateProvider(mockedHttpClientFactory);

        var results = await provider.GetSearchResults(new BookInfo { Name = "深入理解计算机系统" }, CancellationToken.None);

        Assert.Single(results);
        Assert.True(requests.Count >= 2);
        Assert.Contains(requests[0].Headers.UserAgent, value => value.Product?.Name == "Mozilla");
        Assert.Contains(requests[0].Headers.AcceptLanguage, value => value.Value == "zh-CN");
        Assert.True(requests[0].Headers.Contains("Sec-Fetch-Mode"));
        Assert.True(requests[1].Headers.TryGetValues("Cookie", out var cookies));
        Assert.Contains("bid=test-cookie", string.Join(";", cookies));
    }
}
