namespace Jellyfin.Plugin.DoubanBookshelf.Tests.Http
{
    /// <summary>
    /// HttpMessageHandler that returns a mocked response.
    /// </summary>
    internal class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<(Func<Uri, bool> RequestMatcher, MockHttpResponse Response)> _messageHandlers;

        public MockHttpMessageHandler(List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)> messageHandlers)
            : this(messageHandlers, null)
        {
        }

        public MockHttpMessageHandler(List<(Func<Uri, bool> requestMatcher, MockHttpResponse response)> messageHandlers, Action<HttpRequestMessage>? onRequest)
        {
            _messageHandlers = messageHandlers;
            OnRequest = onRequest;
        }

        public Action<HttpRequestMessage>? OnRequest { get; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri == null)
            {
                throw new ArgumentNullException(nameof(request.RequestUri));
            }

            OnRequest?.Invoke(request);

            var response = _messageHandlers.FirstOrDefault(x => x.RequestMatcher(request.RequestUri)).Response;

            if (response == null)
            {
                throw new InvalidOperationException($"No response found for request {request.RequestUri}");
            }

            var httpResponse = new HttpResponseMessage
            {
                StatusCode = response.StatusCode,
                Content = new StringContent(response.Response),
                RequestMessage = request
            };

            if (response.Location is not null)
            {
                httpResponse.Headers.Location = response.Location;
            }

            foreach (var cookie in response.SetCookies)
            {
                httpResponse.Headers.Add("Set-Cookie", cookie);
            }

            return Task.FromResult(httpResponse);
        }
    }

}
