using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.AniList
{
    [TestFixture]
    public class AniListRequestServiceFixture : CoreTest<AniListRequestService>
    {
        private DateTime _now;
        private List<TimeSpan> _waits;
        private HttpRequest _request;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            _waits = new List<TimeSpan>();
            _request = new HttpRequest("https://graphql.anilist.co");

            Mocker.GetMock<IAniListRequestDelay>()
                  .SetupGet(delay => delay.UtcNow)
                  .Returns(() => _now);
            Mocker.GetMock<IAniListRequestDelay>()
                  .Setup(delay => delay.Wait(It.IsAny<TimeSpan>()))
                  .Callback<TimeSpan>(delay =>
                  {
                      _waits.Add(delay);
                      _now += delay;
                  });

            // The first fixture can initialize NzbDroneLogger while constructing
            // Mocker, which replaces the configuration created by the base setup.
            InitLogging();
        }

        private HttpResponse Response(HttpStatusCode status, params string[] headers)
        {
            var responseHeaders = new HttpHeader();

            for (var index = 0; index < headers.Length; index += 2)
            {
                responseHeaders.Add(headers[index], headers[index + 1]);
            }

            return new HttpResponse(_request, responseHeaders, "{\"private\":\"response-data\"}", status);
        }

        private void GivenResponses(bool throwRateLimit, params HttpResponse[] responses)
        {
            var queue = new Queue<HttpResponse>(responses);
            Mocker.GetMock<IHttpClient>()
                  .Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest request) =>
                  {
                      var response = queue.Dequeue();

                      if (throwRateLimit && response.StatusCode == HttpStatusCode.TooManyRequests)
                      {
                          throw new TooManyRequestsException(request, response);
                      }

                      return response;
                  });
        }

        private AniListRequestService AnotherInstance()
        {
            return new AniListRequestService(Mocker.Resolve<IHttpClient>(),
                Mocker.Resolve<IRateLimitService>(),
                Mocker.Resolve<IAniListRequestDelay>(),
                Mocker.Resolve<ICacheManager>(),
                TestLogger);
        }

        [Test]
        public void should_skip_optional_metadata_without_waiting_through_a_shared_long_cooldown()
        {
            GivenResponses(false, Response(HttpStatusCode.TooManyRequests, "Retry-After", "600"));
            Assert.Throws<TooManyRequestsException>(() => Subject.ExecuteOptional(_request));
            Assert.Throws<TooManyRequestsException>(() => AnotherInstance().ExecuteOptional(_request));
            _waits.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Once());
        }

        [Test]
        public void should_share_an_optional_request_quota_with_the_following_normal_list_request()
        {
            GivenResponses(false, Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "0", "Retry-After", "10"), Response(HttpStatusCode.OK));
            Subject.ExecuteOptional(_request);
            AnotherInstance().Execute(_request);
            _waits.Should().Equal(TimeSpan.FromSeconds(10));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(2));
        }

        [Test]
        public void should_share_one_conservative_domain_budget_across_calls()
        {
            GivenResponses(false, Response(HttpStatusCode.OK), Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(new HttpRequest("https://graphql.anilist.co"));

            Mocker.GetMock<IRateLimitService>()
                  .Verify(limiter => limiter.WaitAndPulse("graphql.anilist.co", TimeSpan.FromSeconds(2.1)), Times.Exactly(2));
            _waits.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_retry_a_raw_or_thrown_429_after_the_server_delay(bool throwRateLimit)
        {
            var success = Response(HttpStatusCode.OK);
            GivenResponses(throwRateLimit, Response(HttpStatusCode.TooManyRequests, "Retry-After", "5"), success);

            Subject.Execute(_request).Should().BeSameAs(success);

            _waits.Should().Equal(TimeSpan.FromSeconds(5));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(2));
            Mocker.GetMock<IRateLimitService>()
                  .Verify(limiter => limiter.WaitAndPulse("graphql.anilist.co", TimeSpan.FromSeconds(2.1)), Times.Exactly(2));
        }

        [Test]
        public void should_honor_retry_after_http_date()
        {
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", _now.AddSeconds(7).ToString("R", CultureInfo.InvariantCulture)),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(7));
        }

        [Test]
        public void should_honor_the_longer_server_reset_time()
        {
            var reset = new DateTimeOffset(_now.AddSeconds(12)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "3", "X-RateLimit-Reset", reset),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(12));
        }

        [TestCase("", "")]
        [TestCase("invalid", "invalid")]
        [TestCase("-5", "-1")]
        [TestCase("0", "9223372036854775807")]
        public void should_back_off_when_429_headers_are_missing_or_invalid(string retryAfter, string reset)
        {
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", retryAfter, "X-RateLimit-Reset", reset),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromMinutes(1));
        }

        [Test]
        public void should_propagate_a_clean_typed_error_after_three_attempts()
        {
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "2"),
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "2"),
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "2"));

            var exception = Assert.Throws<TooManyRequestsException>(() => Subject.Execute(_request));

            exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(2));
            exception.ToString().Should().NotContain("response-data");
            _waits.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(3));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_retain_a_long_cooldown_without_blocking_or_retrying_early()
        {
            GivenResponses(false, Response(HttpStatusCode.TooManyRequests, "Retry-After", "600"));

            Assert.Throws<TooManyRequestsException>(() => Subject.Execute(_request))
                  .RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
            Assert.Throws<TooManyRequestsException>(() => AnotherInstance().Execute(_request))
                  .RetryAfter.Should().Be(TimeSpan.FromMinutes(10));

            _waits.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_bound_the_total_cooldown_wait_for_one_call()
        {
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "70"),
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "70"));

            Assert.Throws<TooManyRequestsException>(() => Subject.Execute(_request))
                  .RetryAfter.Should().Be(TimeSpan.FromSeconds(70));

            _waits.Should().Equal(TimeSpan.FromSeconds(70));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_hold_the_next_client_until_a_successful_response_budget_resets()
        {
            var reset = new DateTimeOffset(_now.AddSeconds(15)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "0", "X-RateLimit-Reset", reset),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            _waits.Should().BeEmpty();

            AnotherInstance().Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(15));
        }

        [Test]
        public void should_back_off_when_a_successful_exhausted_budget_omits_reset_headers()
        {
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "0"),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);

            _waits.Should().Equal(TimeSpan.FromMinutes(1));
        }

        [Test]
        public void should_widen_shared_spacing_for_an_advertised_budget_below_thirty_per_minute()
        {
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Limit", "10"),
                Response(HttpStatusCode.OK),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);
            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(6.1), TimeSpan.FromSeconds(6.1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_keep_a_known_lower_budget_when_an_http_or_network_request_fails(bool networkFailure)
        {
            Exception exception = networkFailure
                ? new WebException("Connection failed", WebExceptionStatus.ConnectFailure)
                : new HttpException(_request, Response(HttpStatusCode.InternalServerError));
            Mocker.GetMock<IHttpClient>()
                  .SetupSequence(client => client.Execute(It.IsAny<HttpRequest>()))
                  .Returns(Response(HttpStatusCode.OK, "X-RateLimit-Limit", "10"))
                  .Throws(exception)
                  .Returns(Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            Action failedRequest = () => AnotherInstance().Execute(_request);
            failedRequest.Should().Throw<Exception>().Which.Should().BeSameAs(exception);
            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(6.1), TimeSpan.FromSeconds(6.1));
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(3));
        }

        [TestCase("90")]
        [TestCase("0")]
        [TestCase("invalid")]
        public void should_keep_the_conservative_default_for_higher_or_invalid_server_limits(string limit)
        {
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Limit", limit),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);

            _waits.Should().BeEmpty();
            Mocker.GetMock<IRateLimitService>()
                  .Verify(limiter => limiter.WaitAndPulse("graphql.anilist.co", TimeSpan.FromSeconds(2.1)), Times.Exactly(2));
        }

        [Test]
        public void should_pace_concurrent_service_instances_with_the_real_domain_limiter()
        {
            var cache = new CacheManager();
            var limiter = new RateLimitService(cache, TestLogger);
            var delay = new AniListRequestDelay();
            var first = new AniListRequestService(Mocker.Resolve<IHttpClient>(), limiter, delay, cache, TestLogger);
            var second = new AniListRequestService(Mocker.Resolve<IHttpClient>(), limiter, delay, cache, TestLogger);
            var stopwatch = Stopwatch.StartNew();
            var starts = new List<TimeSpan>();
            var active = 0;
            var overlap = false;
            Mocker.GetMock<IHttpClient>()
                  .Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest request) =>
                  {
                      if (Interlocked.Increment(ref active) != 1)
                      {
                          overlap = true;
                      }

                      lock (starts)
                      {
                          starts.Add(stopwatch.Elapsed);
                      }

                      Thread.Sleep(50);
                      Interlocked.Decrement(ref active);
                      return new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.OK);
                  });

            // Warm only the mock dispatch; leave the real limiter and service state untouched.
            Mocker.GetMock<IHttpClient>().Object.Execute(_request);
            Mocker.GetMock<IHttpClient>().Invocations.Clear();
            starts.Clear();
            active = 0;
            overlap = false;
            stopwatch.Restart();

            Task.WaitAll(Task.Run(() => first.Execute(_request)), Task.Run(() => second.Execute(new HttpRequest("https://graphql.anilist.co"))));

            overlap.Should().BeFalse();
            starts.Should().HaveCount(2);
            (starts[1] - starts[0]).Should().BeGreaterThan(TimeSpan.FromSeconds(2));
        }

        [Test]
        public void should_not_pause_an_available_budget_merely_because_reset_is_in_the_future()
        {
            var reset = new DateTimeOffset(_now.AddSeconds(15)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "12", "X-RateLimit-Reset", reset),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);

            _waits.Should().BeEmpty();
        }

        [Test]
        public void should_spread_a_low_remaining_shared_budget_until_the_advertised_reset()
        {
            var reset = new DateTimeOffset(_now.AddSeconds(40)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "4", "X-RateLimit-Reset", reset),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(10));
        }

        [Test]
        public void should_track_the_observed_remaining_budget_when_success_headers_omit_reset()
        {
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "3"),
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "2"),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            AnotherInstance().Execute(_request);
            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

        [Test]
        public void should_stop_remaining_budget_throttling_after_the_observed_window_expires()
        {
            GivenResponses(false,
                Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "1"),
                Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            _now += TimeSpan.FromSeconds(61);
            AnotherInstance().Execute(_request);

            _waits.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_keep_remaining_budget_spacing_when_a_following_request_fails(bool networkFailure)
        {
            Exception exception = networkFailure
                ? new WebException("Connection failed", WebExceptionStatus.ConnectFailure)
                : new HttpException(_request, Response(HttpStatusCode.InternalServerError));
            Mocker.GetMock<IHttpClient>()
                .SetupSequence(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns(Response(HttpStatusCode.OK, "X-RateLimit-Remaining", "3"))
                .Throws(exception)
                .Returns(Response(HttpStatusCode.OK));

            Subject.Execute(_request);
            Action failedRequest = () => AnotherInstance().Execute(_request);
            failedRequest.Should().Throw<Exception>().Which.Should().BeSameAs(exception);
            Subject.Execute(_request);

            _waits.Should().Equal(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
        }

        [Test]
        public void should_allow_requests_after_a_long_shared_cooldown_expires()
        {
            GivenResponses(false,
                Response(HttpStatusCode.TooManyRequests, "Retry-After", "600"),
                Response(HttpStatusCode.OK));

            Assert.Throws<TooManyRequestsException>(() => Subject.Execute(_request));
            _now += TimeSpan.FromMinutes(10);

            AnotherInstance().Execute(_request).StatusCode.Should().Be(HttpStatusCode.OK);

            _waits.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_the_graphql_request_and_disable_private_body_logging()
        {
            _request.SetContent("{\"query\":\"private-list\"}");
            _request.Headers.Add("Authorization", "Bearer private-access-token");
            _request.ContentSummary = "private-query";
            _request.LogHttpError = true;
            _request.LogResponseContent = true;
            _request.RateLimit = TimeSpan.FromSeconds(2);
            var originalContent = _request.ContentData;
            GivenResponses(false, Response(HttpStatusCode.OK));

            Subject.Execute(_request);

            _request.ContentData.Should().BeSameAs(originalContent);
            _request.Headers["Authorization"].Should().Be("Bearer private-access-token");
            _request.LogHttpError.Should().BeFalse();
            _request.LogResponseContent.Should().BeFalse();
            _request.ContentSummary.Should().BeNull();
            _request.RateLimit.Should().Be(TimeSpan.Zero);
        }

        [Test]
        public void should_not_retry_other_http_failures()
        {
            var response = Response(HttpStatusCode.Unauthorized);
            var exception = new HttpException(_request, response);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(_request)).Throws(exception);

            Assert.Throws<HttpException>(() => Subject.Execute(_request)).Should().BeSameAs(exception);

            _waits.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Execute(_request), Times.Once());
        }
    }
}
