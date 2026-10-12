using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinProxy
    {
        IList<JellyfinLibrary> GetLibraries(JellyfinSettings settings);
        void RefreshLibrary(JellyfinSettings settings);
    }

    public class JellyfinProxy : IJellyfinProxy
    {
        private readonly IHttpClient _httpClient;

        public JellyfinProxy(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public IList<JellyfinLibrary> GetLibraries(JellyfinSettings settings)
        {
            settings.Validate().Filter("Host", "Port", "UseSsl", "UrlBase", "ApiKey").ThrowOnError();

            try
            {
                var request = BuildRequest("Library/VirtualFolders", settings).Build();
                var response = _httpClient.Get<List<JellyfinLibrary>>(request);
                EnsureSuccess(response);

                if (response.StatusCode != HttpStatusCode.OK || response.Resource == null)
                {
                    throw new JellyfinException("Host", "Jellyfin returned an invalid library list.");
                }

                return response.Resource.Where(c => c != null && Guid.TryParse(c.ItemId, out var id) && id != Guid.Empty).ToList();
            }
            catch (JellyfinException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new JellyfinException("Host", "Unable to retrieve Jellyfin libraries. Check the server address, TLS configuration, and API key.");
            }
        }

        public void RefreshLibrary(JellyfinSettings settings)
        {
            if (!Guid.TryParse(settings.LibraryId, out var libraryId) || libraryId == Guid.Empty)
            {
                throw new JellyfinException("LibraryId", "Select a Jellyfin library.");
            }

            var libraries = GetLibraries(settings);

            if (!libraries.Any(c => Guid.TryParse(c.ItemId, out var itemId) && itemId == libraryId))
            {
                throw new JellyfinException("LibraryId", "The selected Jellyfin library no longer exists or is not accessible. Select a library again.");
            }

            try
            {
                var request = BuildRequest($"Items/{libraryId:N}/Refresh", settings)
                    .AddQueryParam("metadataRefreshMode", "FullRefresh")
                    .AddQueryParam("imageRefreshMode", "Default")
                    .AddQueryParam("replaceAllMetadata", "false")
                    .AddQueryParam("replaceAllImages", "false")
                    .Build();

                var response = _httpClient.Post(request);
                EnsureSuccess(response);

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    throw new JellyfinException("Host", "Jellyfin did not confirm the library refresh with HTTP 204. Check the server and proxy configuration; pending imports will be retried.");
                }
            }
            catch (JellyfinException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new JellyfinException("Host", "Unable to request a Jellyfin library refresh. The pending import will be retried.");
            }
        }

        private static HttpRequestBuilder BuildRequest(string path, JellyfinSettings settings)
        {
            var request = new HttpRequestBuilder(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase)
            {
                AllowAutoRedirect = false,
                SuppressHttpError = true,
                LogHttpError = false,
                LogResponseContent = false,
                StoreRequestCookie = false,
                PostProcess = c => c.RequestTimeout = TimeSpan.FromSeconds(30)
            };

            request.Headers.Add("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
            return request.Resource(path);
        }

        private static void EnsureSuccess(HttpResponse response)
        {
            var statusCode = (int)response.StatusCode;

            if (statusCode is 401 or 403)
            {
                throw new JellyfinException("ApiKey", "Jellyfin rejected the API key or denied administrator access.");
            }

            if (statusCode < 200 || statusCode >= 300)
            {
                throw new JellyfinException("Host", $"Jellyfin returned HTTP {statusCode}. Check the server address and proxy configuration; redirects are not followed.");
            }
        }
    }
}
