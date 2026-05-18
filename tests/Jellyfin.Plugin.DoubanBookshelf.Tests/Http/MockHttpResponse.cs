using System.Net;

namespace Jellyfin.Plugin.DoubanBookshelf.Tests.Http
{
    internal record MockHttpResponse(HttpStatusCode StatusCode, string Response)
    {
        public Uri? Location { get; init; }

        public string? ContentType { get; init; }

        public IReadOnlyList<string> SetCookies { get; init; } = [];
    }
}
