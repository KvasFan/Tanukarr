using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class MyAnimeListRequestServiceFixture : CoreTest<MyAnimeListRequestService>
    {
        private readonly List<TimeSpan> _waits = new();
        private DateTime _now;
        private int _calls;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
            _waits.Clear();
            _calls = 0;
            Mocker.GetMock<IMyAnimeListRequestDelay>().SetupGet(delay => delay.UtcNow).Returns(() => _now);
            Mocker.GetMock<IMyAnimeListRequestDelay>().Setup(delay => delay.Wait(It.IsAny<TimeSpan>()))
                .Callback((TimeSpan duration) =>
                {
                    _waits.Add(duration);
                    _now += duration;
                });
        }

        private void GivenLimited(int count, string retryAfter = "1")
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request,
                    new HttpHeader { ["Retry-After"] = retryAfter },
                    "{\"message\":\"generated quota response\"}",
                    ++_calls <= count ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));
        }

        [Test]
        public void should_honor_retry_after_and_recover_with_bounded_retries_and_private_logging()
        {
            GivenLimited(2);
            var request = new HttpRequest("https://api.myanimelist.net/v2/users/@me/animelist") { ContentSummary = "generated private content" };
            Subject.Execute(request).StatusCode.Should().Be(HttpStatusCode.OK);
            _calls.Should().Be(3);
            _waits.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            request.LogResponseContent.Should().BeFalse();
            request.LogHttpError.Should().BeFalse();
            request.ContentSummary.Should().BeNull();
            request.AllowAutoRedirect.Should().BeFalse();
        }

        [Test]
        public void should_stop_after_three_terminal_429_attempts()
        {
            GivenLimited(10);
            Action act = () => Subject.Execute(new HttpRequest("https://api.myanimelist.net/v2/users/@me/animelist"));
            act.Should().Throw<TooManyRequestsException>();
            _calls.Should().Be(3);
            _waits.Should().HaveCount(2);
        }

        [Test]
        public void should_share_long_cooldown_across_provider_instances_without_waiting_or_sending_more_requests()
        {
            GivenLimited(10, "120");
            Action first = () => Subject.Execute(new HttpRequest("https://api.myanimelist.net/v2/users/@me/animelist"));
            first.Should().Throw<TooManyRequestsException>();
            var second = Mocker.Resolve<MyAnimeListRequestService>();
            Action retry = () => second.Execute(new HttpRequest("https://api.myanimelist.net/v2/users/Public-user/animelist"));
            retry.Should().Throw<TooManyRequestsException>();
            _calls.Should().Be(1);
            _waits.Should().BeEmpty();
        }
    }
}
