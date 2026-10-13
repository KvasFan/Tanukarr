using System;
using System.Net;
using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public static class SonarrCloudRequestPolicy
    {
        private const string DisabledMessage = "Requests to Sonarr's shared cloud services are disabled in this fork.";

        public static void EnsureAllowed(HttpRequest request)
        {
            if (!IsBlockedHost(request.Url.Host))
            {
                return;
            }

            var response = new HttpResponse(request, new HttpHeader(), DisabledMessage, HttpStatusCode.Forbidden);

            throw new HttpException(request, response, DisabledMessage);
        }

        public static bool IsExpectedBlock(HttpException exception)
        {
            return exception?.Request?.Url != null &&
                   IsBlockedHost(exception.Request.Url.Host) &&
                   exception.Response?.StatusCode == HttpStatusCode.Forbidden &&
                   exception.Response.ResponseData != null &&
                   string.Equals(exception.Message, DisabledMessage, StringComparison.Ordinal) &&
                   string.Equals(exception.Response.Content, DisabledMessage, StringComparison.Ordinal);
        }

        private static bool IsBlockedHost(string value)
        {
            var host = value.TrimEnd('.');
            return host.Equals("services.sonarr.tv", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("sentry.sonarr.tv", StringComparison.OrdinalIgnoreCase);
        }
    }
}
