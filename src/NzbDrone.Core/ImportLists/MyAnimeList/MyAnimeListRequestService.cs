using System;
using System.Net;
using System.Threading;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    public interface IMyAnimeListRequestDelay
    {
        DateTime UtcNow { get; }
        void Wait(TimeSpan duration);
    }

    public class MyAnimeListRequestDelay : IMyAnimeListRequestDelay
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public void Wait(TimeSpan duration) => Thread.Sleep(duration);
    }

    public interface IMyAnimeListRequestService
    {
        HttpResponse Execute(HttpRequest request);
    }

    public class MyAnimeListRequestService : IMyAnimeListRequestService
    {
        private readonly IHttpClient _httpClient;
        private readonly IRateLimitService _rateLimitService;
        private readonly IMyAnimeListRequestDelay _delay;
        private readonly ICached<RequestState> _states;

        public MyAnimeListRequestService(IHttpClient httpClient, IRateLimitService rateLimitService, IMyAnimeListRequestDelay delay, ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _rateLimitService = rateLimitService;
            _delay = delay;
            _states = cacheManager.GetCache<RequestState>(typeof(MyAnimeListRequestService));
        }

        public HttpResponse Execute(HttpRequest request)
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
                for (var attempt = 1; attempt <= 3; attempt++)
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
                    if (attempt == 3)
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
}
