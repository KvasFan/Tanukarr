using System;
using System.Net;
using System.Threading;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.ImportLists.Simkl.User;

namespace NzbDrone.Core.ImportLists.Simkl
{
    public interface ISimklV2RequestDelay
    {
        DateTime UtcNow { get; }
        void Wait(TimeSpan duration);
    }

    public class SimklV2RequestDelay : ISimklV2RequestDelay
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public void Wait(TimeSpan duration) => Thread.Sleep(duration);
    }

    public interface ISimklV2RequestService
    {
        HttpResponse Execute(HttpRequest request);
        HttpResponse ExecuteOnce(HttpRequest request);
    }

    public class SimklV2RequestService : ISimklV2RequestService
    {
        private readonly IHttpClient _httpClient;
        private readonly IRateLimitService _rateLimitService;
        private readonly ISimklV2RequestDelay _delay;
        private readonly ICached<RequestState> _states;

        public SimklV2RequestService(IHttpClient httpClient, IRateLimitService rateLimitService, ISimklV2RequestDelay delay, ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _rateLimitService = rateLimitService;
            _delay = delay;
            _states = cacheManager.GetCache<RequestState>(typeof(SimklV2RequestService));
        }

        public HttpResponse Execute(HttpRequest request) => Execute(request, 3);
        public HttpResponse ExecuteOnce(HttpRequest request) => Execute(request, 1);

        private HttpResponse Execute(HttpRequest request, int attempts)
        {
            var host = new Uri(request.Url.FullUri).Host;
            var state = _states.Get(host, () => new RequestState());
            lock (state)
            {
                request.LogHttpError = false;
                request.LogResponseContent = false;
                request.ContentSummary = null;
                request.AllowAutoRedirect = false;
                request.SuppressHttpError = true;
                request.RateLimit = TimeSpan.Zero;
                var deadline = _delay.UtcNow.AddSeconds(30);
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    if (state.NotBefore > deadline)
                    {
                        throw new TooManyRequestsException(request, state.LastLimitedResponse);
                    }

                    if (state.NotBefore > _delay.UtcNow)
                    {
                        _delay.Wait(state.NotBefore - _delay.UtcNow);
                    }

                    _rateLimitService.WaitAndPulse(host, TimeSpan.FromSeconds(2));
                    var response = _httpClient.Execute(request);
                    if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        return response;
                    }

                    var limited = new TooManyRequestsException(request, response);
                    var wait = limited.RetryAfter > TimeSpan.Zero ? limited.RetryAfter : TimeSpan.FromSeconds(5);
                    state.LastLimitedResponse = response;
                    state.NotBefore = _delay.UtcNow.Add(wait);
                    if (attempt == attempts)
                    {
                        throw limited;
                    }
                }

                throw new TooManyRequestsException(request, state.LastLimitedResponse);
            }
        }

        private sealed class RequestState
        {
            public DateTime NotBefore { get; set; }
            public HttpResponse LastLimitedResponse { get; set; }
        }
    }

    public static class SimklV2RequestBuilder
    {
        public static HttpRequest Build(string resource, SimklUserSettings settings)
        {
            var request = new HttpRequestBuilder(settings.BaseUrl.Trim()).Resource(resource.TrimStart('/'))
                .AddQueryParam("client_id", settings.ClientIdV2?.Trim())
                .AddQueryParam("app-name", "Tanukarr")
                .AddQueryParam("app-version", BuildInfo.ProductVersion).Accept(HttpAccept.Json).Build();
            request.Headers.Add("Authorization", "Bearer " + settings.AccessToken);
            request.Headers["User-Agent"] = "Tanukarr/" + BuildInfo.ProductVersion;
            return request;
        }
    }
}
