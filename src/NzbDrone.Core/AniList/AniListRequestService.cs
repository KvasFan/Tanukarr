using System;
using System.Globalization;
using System.Net;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;

namespace NzbDrone.Core.AniList
{
    public interface IAniListRequestService
    {
        HttpResponse Execute(HttpRequest request);
    }

    public class AniListRequestService : IAniListRequestService
    {
        private const int MaxAttempts = 3;
        private const string RateLimitKey = "graphql.anilist.co";
        private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(2.1);
        private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MaxRequestWait = TimeSpan.FromMinutes(2);

        private readonly IHttpClient _httpClient;
        private readonly IRateLimitService _rateLimitService;
        private readonly IAniListRequestDelay _delay;
        private readonly RequestState _state;
        private readonly Logger _logger;

        public AniListRequestService(IHttpClient httpClient,
                                     IRateLimitService rateLimitService,
                                     IAniListRequestDelay delay,
                                     ICacheManager cacheManager,
                                     Logger logger)
        {
            _httpClient = httpClient;
            _rateLimitService = rateLimitService;
            _delay = delay;
            _logger = logger;
            _state = cacheManager.GetCache<RequestState>(typeof(AniListRequestService), "requestState")
                .Get("graphql", () => new RequestState());
        }

        public HttpResponse Execute(HttpRequest request)
        {
            // List tests, list pagination, metadata and search all consume the same
            // server budget. Share both the lock and cooldown across service instances.
            lock (_state.SyncRoot)
            {
                var deadline = _delay.UtcNow.Add(MaxRequestWait);

                // HTTP logging must not include private GraphQL response bodies.
                request.LogHttpError = false;
                request.LogResponseContent = false;
                request.ContentSummary = null;
                request.RateLimit = TimeSpan.Zero;

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    WaitForCooldown(request, deadline);
                    _rateLimitService.WaitAndPulse(RateLimitKey, RequestInterval);
                    _state.LastRequestAt = _delay.UtcNow;

                    if (_delay.UtcNow > deadline)
                    {
                        throw RateLimitException(request);
                    }

                    // Failed HTTP requests can still consume the server allowance.
                    // Reserve a known lower budget before dispatch, including errors.
                    var effectiveInterval = EffectiveInterval();

                    if (effectiveInterval > RequestInterval)
                    {
                        var nextRequest = _state.LastRequestAt.Add(effectiveInterval);
                        _state.NotBefore = nextRequest > _state.NotBefore ? nextRequest : _state.NotBefore;
                    }

                    if (_state.BudgetResetAt > _delay.UtcNow && _state.LastRemaining > 0)
                    {
                        // Reserve a response-observed allowance even when the next
                        // request fails before returning updated quota headers.
                        _state.LastRemaining--;

                        if (_state.LastRemaining == 0 && _state.BudgetResetAt > _state.NotBefore)
                        {
                            _state.NotBefore = _state.BudgetResetAt;
                        }
                    }

                    HttpResponse response;

                    try
                    {
                        response = _httpClient.Execute(request);
                    }
                    catch (TooManyRequestsException ex)
                    {
                        response = ex.Response;
                    }

                    UpdateAdvertisedBudget(response);

                    if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        if (int.TryParse(response.Headers["X-RateLimit-Remaining"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining) && remaining == 0)
                        {
                            ExtendCooldown(response);
                        }

                        return response;
                    }

                    ExtendCooldown(response);

                    if (attempt == MaxAttempts || _state.NotBefore > deadline)
                    {
                        _logger.Warn("AniList request limit reached; retry after {0:0} seconds", RemainingCooldown().TotalSeconds);
                        throw RateLimitException(request);
                    }

                    _logger.Debug("AniList request limit reached; waiting {0:0} seconds before retry {1}/{2}", RemainingCooldown().TotalSeconds, attempt + 1, MaxAttempts);
                }

                throw RateLimitException(request);
            }
        }

        private void WaitForCooldown(HttpRequest request, DateTime deadline)
        {
            // Account for the existing domain limiter before entering a blocking wait.
            var nextRequest = _state.LastRequestAt == default ? default : _state.LastRequestAt.Add(RequestInterval);

            if (nextRequest > deadline)
            {
                _state.NotBefore = nextRequest > _state.NotBefore ? nextRequest : _state.NotBefore;
                throw RateLimitException(request);
            }

            var remaining = RemainingCooldown();

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (_state.NotBefore > deadline)
            {
                throw RateLimitException(request);
            }

            _delay.Wait(remaining);
        }

        private void UpdateAdvertisedBudget(HttpResponse response)
        {
            var now = _delay.UtcNow;

            if (int.TryParse(response.Headers["X-RateLimit-Limit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) && limit > 0)
            {
                // A lower advertised allowance needs wider spacing. Never speed up
                // past the conservative default when the server advertises 90/min.
                var interval = TimeSpan.FromSeconds(61d / limit);
                _state.AdvertisedInterval = interval > RequestInterval ? interval : RequestInterval;
            }

            if (int.TryParse(response.Headers["X-RateLimit-Remaining"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining) && remaining >= 0)
            {
                var reset = GetResetTime(response);

                if (reset > now)
                {
                    _state.BudgetResetAt = reset;
                }
                else if (_state.BudgetResetAt <= now || remaining > _state.LastRemaining)
                {
                    // Successful AniList responses often omit reset. Track the
                    // observed window instead of assuming each request starts one.
                    _state.BudgetResetAt = now.Add(DefaultCooldown);
                }

                _state.LastRemaining = remaining;
                _state.BudgetInterval = remaining > 0
                    ? TimeSpan.FromTicks((_state.BudgetResetAt - now).Ticks / remaining)
                    : RequestInterval;
            }

            var effectiveInterval = EffectiveInterval();

            if (effectiveInterval > RequestInterval)
            {
                var nextRequest = now.Add(effectiveInterval);

                if (nextRequest > _state.NotBefore)
                {
                    _state.NotBefore = nextRequest;
                }
            }
        }

        private TimeSpan EffectiveInterval()
        {
            if (_state.BudgetResetAt <= _delay.UtcNow)
            {
                _state.BudgetInterval = RequestInterval;
            }

            return _state.AdvertisedInterval > _state.BudgetInterval
                ? _state.AdvertisedInterval
                : _state.BudgetInterval;
        }

        private static DateTime GetResetTime(HttpResponse response)
        {
            if (long.TryParse(response.Headers["X-RateLimit-Reset"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetSeconds))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds(resetSeconds).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // An invalid timestamp cannot override the observed budget.
                }
            }

            return default;
        }

        private void ExtendCooldown(HttpResponse response)
        {
            var now = _delay.UtcNow;
            var notBefore = now;
            var retryAfter = response.Headers["Retry-After"];

            if (int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                notBefore = now.AddSeconds(seconds);
            }
            else if (DateTimeOffset.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryDate))
            {
                notBefore = retryDate.UtcDateTime;
            }

            if (long.TryParse(response.Headers["X-RateLimit-Reset"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetSeconds))
            {
                try
                {
                    var reset = DateTimeOffset.FromUnixTimeSeconds(resetSeconds).UtcDateTime;

                    if (reset > notBefore)
                    {
                        notBefore = reset;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Ignore malformed reset timestamps; an actual 429 still backs off.
                }
            }

            if (notBefore <= now)
            {
                notBefore = now.Add(DefaultCooldown);
            }

            if (notBefore > _state.NotBefore)
            {
                _state.NotBefore = notBefore;
            }
        }

        private TimeSpan RemainingCooldown()
        {
            var remaining = _state.NotBefore - _delay.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private TooManyRequestsException RateLimitException(HttpRequest request)
        {
            var headers = new HttpHeader
            {
                { "Retry-After", Math.Clamp(Math.Ceiling(RemainingCooldown().TotalSeconds), 1, int.MaxValue).ToString(CultureInfo.InvariantCulture) }
            };

            // A provider may echo sensitive request data in an error response. Only a
            // safe message and the retry delay need to cross the provider boundary.
            var response = new HttpResponse(request, headers, "AniList request limit reached. Retry later.", HttpStatusCode.TooManyRequests);
            return new TooManyRequestsException(request, response);
        }

        private sealed class RequestState
        {
            public object SyncRoot { get; } = new object();
            public DateTime NotBefore { get; set; }
            public DateTime LastRequestAt { get; set; }
            public TimeSpan AdvertisedInterval { get; set; } = RequestInterval;
            public DateTime BudgetResetAt { get; set; }
            public int LastRemaining { get; set; }
            public TimeSpan BudgetInterval { get; set; } = RequestInterval;
        }
    }
}
