using System.Net;
using Jellyfin.Plugin.DoubanBookshelf.Providers.Douban;
using Jellyfin.Plugin.DoubanBookshelf.Tests.Http;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jellyfin.Plugin.DoubanBookshelf.Tests;

public class DoubanBooksImageProviderTests
{
    [Fact]
    public async Task GetImages_WithDoubanId_ReturnsCover()
    {
        var handler = new MockHttpMessageHandler(new List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)>
        {
            (uri => uri.AbsoluteUri.Contains("subject/26912767", StringComparison.Ordinal), new MockHttpResponse(HttpStatusCode.OK, TestHelpers.GetFixture("douban-book-detail.html")))
        });
        var mockedHttpClientFactory = Substitute.For<IHttpClientFactory>();
        using var client = new HttpClient(handler);
        mockedHttpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);
        var doubanClient = new DoubanClient(mockedHttpClientFactory, NullLogger<DoubanClient>.Instance, new DoubanBookParser());

        IRemoteImageProvider provider = new DoubanBooksImageProvider(doubanClient);

        var images = await provider.GetImages(new Book { ProviderIds = { { DoubanConstants.ProviderId, "26912767" } } }, CancellationToken.None);

        Assert.Collection(
            images,
            image =>
            {
                Assert.Equal(DoubanConstants.ProviderName, image.ProviderName);
                Assert.Equal("https://img1.doubanio.com/view/subject/l/public/s29195878.jpg", image.Url);
            });
    }
}
