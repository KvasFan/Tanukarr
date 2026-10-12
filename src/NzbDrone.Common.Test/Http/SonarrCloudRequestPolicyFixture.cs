using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Test.Common;
using HttpClient = NzbDrone.Common.Http.HttpClient;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class SonarrCloudRequestPolicyFixture : TestBase<HttpClient>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(Array.Empty<IHttpRequestInterceptor>());
        }

        [TestCase("https://services.sonarr.tv/v1/scenemapping")]
        [TestCase("http://services.sonarr.tv/v1/scenemapping")]
        [TestCase("https://SERVICES.SONARR.TV./v1/scenemapping")]
        [TestCase("https://sentry.sonarr.tv/api/")]
        [TestCase("http://sentry.sonarr.tv/api/")]
        [TestCase("https://SENTRY.SONARR.TV./api/")]
        public void should_block_direct_requests_before_dispatch(string url)
        {
            var request = new HttpRequest(url);

            var exception = Assert.ThrowsAsync<HttpException>(async () => await Subject.ExecuteAsync(request));

            exception.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            VerifyDispatchCount(Times.Never());
        }

        [TestCase("https://services.sonarr.tv/v1/")]
        [TestCase("https://sentry.sonarr.tv/api/")]
        public void should_block_redirects_before_dispatching_to_a_disabled_host(string redirectUrl)
        {
            var request = new HttpRequest("https://example.test/redirect") { AllowAutoRedirect = true };
            var headers = new HttpHeader();
            headers.Add("Location", redirectUrl);

            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponse(request, headers, string.Empty, HttpStatusCode.Found));

            Assert.ThrowsAsync<HttpException>(async () => await Subject.ExecuteAsync(request));

            VerifyDispatchCount(Times.Once());
        }

        [Test]
        public void should_check_the_url_after_request_interceptors()
        {
            var request = new HttpRequest("https://example.test/");
            var interceptor = new Mock<IHttpRequestInterceptor>();
            interceptor.Setup(value => value.PreRequest(request))
                .Returns(new HttpRequest("https://services.sonarr.tv/v1/"));
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(new[] { interceptor.Object });

            Assert.ThrowsAsync<HttpException>(async () => await Subject.ExecuteAsync(request));

            VerifyDispatchCount(Times.Never());
        }

        [Test]
        public void should_not_include_query_secrets_in_the_error()
        {
            var request = new HttpRequest("https://services.sonarr.tv/v1/oauth?refresh_token=private-value");

            var exception = Assert.ThrowsAsync<HttpException>(async () => await Subject.ExecuteAsync(request));

            exception.Message.Should().NotContain("refresh_token").And.NotContain("private-value");
            exception.ToString().Should().NotContain("refresh_token").And.NotContain("private-value");
        }

        [Test]
        public void should_allow_cloud_factory_construction_but_block_services_urls()
        {
            var builder = new SonarrCloudRequestBuilder();

            Assert.Throws<HttpException>(() => builder.Services.Create().Resource("oauth/myanimelist/authorize").Build());
        }

        [Test]
        public async Task should_keep_skyhook_requests_available()
        {
            var request = new SonarrCloudRequestBuilder().SkyHookTvdb.Create().SetSegment("route", "en").Resource("1").Build();

            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(request, It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponse(request, new HttpHeader(), "{}"));

            var response = await Subject.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            VerifyDispatchCount(Times.Once());
        }

        private void VerifyDispatchCount(Times times)
        {
            Mocker.GetMock<IHttpDispatcher>()
                .Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()), times);
        }
    }
}
