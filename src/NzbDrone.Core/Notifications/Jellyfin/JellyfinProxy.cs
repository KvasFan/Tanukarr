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
        void TestLibrary(JellyfinSettings settings);
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

        public void TestLibrary(JellyfinSettings settings)
        {
            var library = GetSelectedLibrary(settings);

            if (library.Locations == null || library.Locations.Length == 0 || library.Locations.Any(string.IsNullOrEmpty))
            {
                throw LibraryNotInitialized();
            }

            var libraryId = Guid.Parse(library.ItemId);
            var missingLocations = library.Locations.Distinct().ToList();

            try
            {
                var startIndex = 0;
                int? totalRecordCount = null;

                while (true)
                {
                    var request = BuildRequest("Items", settings)
                        .AddQueryParam("parentId", libraryId.ToString("N"))
                        .AddQueryParam("recursive", "true")
                        .AddQueryParam("includeItemTypes", "Folder")
                        .AddQueryParam("fields", "Path")
                        .AddQueryParam("startIndex", startIndex)
                        .AddQueryParam("limit", 200)
                        .AddQueryParam("enableTotalRecordCount", "true")
                        .Build();

                    var response = _httpClient.Get<JellyfinItems>(request);
                    EnsureSuccess(response);

                    if (response.StatusCode != HttpStatusCode.OK || response.Resource?.Items == null ||
                        response.Resource.TotalRecordCount < 0 || response.Resource.Items.Count > 200)
                    {
                        throw new JellyfinException("Host", "Jellyfin returned an invalid physical-folder list for the selected library.");
                    }

                    var items = response.Resource.Items;
                    totalRecordCount ??= response.Resource.TotalRecordCount;
                    missingLocations.RemoveAll(location => items.Any(item => item != null &&
                        string.Equals(item.Type, "Folder", StringComparison.OrdinalIgnoreCase) &&
                        SamePhysicalPath(location, item.Path)));

                    if (missingLocations.Count == 0)
                    {
                        return;
                    }

                    var nextIndex = (long)startIndex + items.Count;

                    if (items.Count == 0 || nextIndex >= totalRecordCount.Value)
                    {
                        break;
                    }

                    if (nextIndex <= startIndex || nextIndex > int.MaxValue)
                    {
                        throw new JellyfinException("Host", "Jellyfin returned invalid physical-folder pagination for the selected library.");
                    }

                    startIndex = (int)nextIndex;
                }

                throw LibraryNotInitialized();
            }
            catch (JellyfinException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new JellyfinException("Host", "Unable to verify physical folders for the selected Jellyfin library. Check the server address, TLS configuration, and API key.");
            }
        }

        public void RefreshLibrary(JellyfinSettings settings)
        {
            TestLibrary(settings);
            var libraryId = Guid.Parse(settings.LibraryId);

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

        private JellyfinLibrary GetSelectedLibrary(JellyfinSettings settings)
        {
            if (!Guid.TryParse(settings.LibraryId, out var libraryId) || libraryId == Guid.Empty)
            {
                throw new JellyfinException("LibraryId", "Select a Jellyfin library.");
            }

            var libraries = GetLibraries(settings)
                .Where(c => Guid.TryParse(c.ItemId, out var itemId) && itemId == libraryId)
                .ToList();

            if (libraries.Count == 0)
            {
                throw new JellyfinException("LibraryId", "The selected Jellyfin library no longer exists or is not accessible. Select a library again.");
            }

            if (libraries.Count != 1)
            {
                throw new JellyfinException("Host", "Jellyfin returned an ambiguous selected-library identifier.");
            }

            return libraries[0];
        }

        private static JellyfinException LibraryNotInitialized()
        {
            return new JellyfinException("LibraryId", "Initialize every folder of this library with a scan in Jellyfin after it contains media, then test again. No server-wide scan will be requested.");
        }

        private static bool SamePhysicalPath(string location, string itemPath)
        {
            if (string.IsNullOrEmpty(itemPath))
            {
                return false;
            }

            var isWindowsPath = location.Contains('\\') ||
                (location.Length >= 2 && char.IsLetter(location[0]) && location[1] == ':');

            if (isWindowsPath)
            {
                return string.Equals(
                    location.Replace('/', '\\').TrimEnd('\\'),
                    itemPath.Replace('/', '\\').TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(location.TrimEnd('/'), itemPath.TrimEnd('/'), StringComparison.Ordinal);
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
