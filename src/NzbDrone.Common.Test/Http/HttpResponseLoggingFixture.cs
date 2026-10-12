using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpResponseLoggingFixture
    {
        [TestCase(HttpStatusCode.Unauthorized, "application/json")]
        [TestCase(HttpStatusCode.InternalServerError, "text/plain")]
        public void should_omit_error_content_when_error_logging_is_disabled(HttpStatusCode status, string contentType)
        {
            var request = new HttpRequest("https://jellyfin.example/Library/VirtualFolders")
            {
                LogHttpError = false,
                LogResponseContent = false
            };
            var response = new HttpResponse(request, new HttpHeader { ContentType = contentType }, "reflected-private-api-key", status);

            response.ToString().Should().Contain(((int)status).ToString()).And.NotContain("reflected-private-api-key");
        }

        [Test]
        public void should_preserve_normal_error_diagnostics_when_enabled()
        {
            var request = new HttpRequest("https://server.example/status");
            var response = new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "ordinary-error-detail", HttpStatusCode.BadRequest);

            response.ToString().Should().Contain("ordinary-error-detail");
        }
    }
}
