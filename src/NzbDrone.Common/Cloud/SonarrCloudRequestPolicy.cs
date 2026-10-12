using System;
using System.Net;
using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public static class SonarrCloudRequestPolicy
    {
        public static void EnsureAllowed(HttpRequest request)
        {
            var host = request.Url.Host.TrimEnd('.');

            if (!host.Equals("services.sonarr.tv", StringComparison.OrdinalIgnoreCase) &&
                !host.Equals("sentry.sonarr.tv", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            const string message = "Requests to Sonarr's shared cloud services are disabled in this fork.";
            var response = new HttpResponse(request, new HttpHeader(), message, HttpStatusCode.Forbidden);

            throw new HttpException(request, response, message);
        }
    }
}
