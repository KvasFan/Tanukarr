using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklV2RequestServiceFixture : CoreTest<SimklV2RequestService>
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
            Mocker.GetMock<ISimklV2RequestDelay>().SetupGet(delay => delay.UtcNow).Returns(() => _now);
            Mocker.GetMock<ISimklV2RequestDelay>().Setup(delay => delay.Wait(It.IsAny<TimeSpan>()))
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
                    "{\"error\":\"generated quota response\"}",
                    ++_calls <= count ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));
        }

        [Test]
        public void should_bound_retries_and_keep_private_provider_content_out_of_logs()
        {
            GivenLimited(2);
            var request = new HttpRequest("https://api.simkl.com/lists/71") { ContentSummary = "generated private content" };
            Subject.Execute(request).StatusCode.Should().Be(HttpStatusCode.OK);
            _calls.Should().Be(3);
            _waits.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            request.LogResponseContent.Should().BeFalse();
            request.LogHttpError.Should().BeFalse();
            request.ContentSummary.Should().BeNull();
            request.AllowAutoRedirect.Should().BeFalse();
        }

        [Test]
        public void should_not_replay_an_authorization_code_post_after_429()
        {
            GivenLimited(10);
            Action act = () => Subject.ExecuteOnce(new HttpRequest("https://api.simkl.com/oauth2/token"));
            act.Should().Throw<TooManyRequestsException>();
            _calls.Should().Be(1);
            _waits.Should().BeEmpty();
        }

        [Test]
        public void should_share_a_long_cooldown_without_more_http_or_blocking_waits()
        {
            GivenLimited(10, "120");
            Action first = () => Subject.Execute(new HttpRequest("https://api.simkl.com/lists/71"));
            first.Should().Throw<TooManyRequestsException>();
            Action second = () => Mocker.Resolve<SimklV2RequestService>().Execute(new HttpRequest("https://api.simkl.com/lists/72"));
            second.Should().Throw<TooManyRequestsException>();
            _calls.Should().Be(1);
            _waits.Should().BeEmpty();
        }
    }
}
