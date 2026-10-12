using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Test.Common;
using HttpClient = NzbDrone.Common.Http.HttpClient;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpClientSuppressedLoggingFixture : TestBase<HttpClient>
    {
        [Test]
        public async Task should_not_log_a_reflected_redirect_target_when_error_logging_is_disabled()
        {
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(Array.Empty<IHttpRequestInterceptor>());
            var request = new HttpRequest("https://jellyfin.example/Library/VirtualFolders")
            {
                AllowAutoRedirect = false,
                LogHttpError = false
            };
            var headers = new HttpHeader();
            headers.Add("Location", "https://other.example/reflected-private-key");
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(request, It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponse(request, headers, string.Empty, HttpStatusCode.Found));

            var response = await Subject.ExecuteAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Found);
            Mocker.GetMock<IHttpDispatcher>()
                .Verify(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>(), It.IsAny<CancellationToken>()), Times.Once());

            // TestBase fails this test if the developer-only redirect error is logged.
        }
    }
}
