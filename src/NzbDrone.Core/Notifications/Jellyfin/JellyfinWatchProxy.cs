using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinWatchProxy
    {
        IList<JellyfinUser> GetUsers(JellyfinSettings settings);
        IList<JellyfinWatchedItem> GetSnapshot(JellyfinSettings settings);
    }

    public class JellyfinWatchProxy : IJellyfinWatchProxy
    {
        private const int PageSize = 200;
        private const int MaximumItems = 100000;
        private readonly IHttpClient _httpClient;
        private readonly IJellyfinProxy _jellyfinProxy;

        public JellyfinWatchProxy(IHttpClient httpClient, IJellyfinProxy jellyfinProxy)
        {
            _httpClient = httpClient;
            _jellyfinProxy = jellyfinProxy;
        }

        public IList<JellyfinUser> GetUsers(JellyfinSettings settings)
        {
            settings.Validate().Filter("Host", "Port", "UseSsl", "UrlBase", "ApiKey").ThrowOnError();
            try
            {
                var response = _httpClient.Get<List<JellyfinUser>>(JellyfinProxy.BuildRequest("Users", settings).Build());
                JellyfinProxy.EnsureSuccess(response);
                if (response.StatusCode != HttpStatusCode.OK || response.Resource == null ||
                    response.Resource.Any(user => user == null || !ValidId(user.Id) || string.IsNullOrEmpty(user.Name)) ||
                    response.Resource.Select(user => Guid.Parse(user.Id)).Distinct().Count() != response.Resource.Count)
                {
                    throw new JellyfinException("WatchedUserId", "Jellyfin returned an invalid user list.");
                }

                return response.Resource;
            }
            catch (JellyfinException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new JellyfinException("Host", "Unable to retrieve Jellyfin users. Check the connection and administrator API key.");
            }
        }

        public IList<JellyfinWatchedItem> GetSnapshot(JellyfinSettings settings)
        {
            settings.Validate().ThrowOnError();
            if (!ValidId(settings.WatchedUserId))
            {
                throw new JellyfinException("WatchedUserId", "Select a Jellyfin user before synchronizing watched status.");
            }

            try
            {
                var libraryId = Guid.Parse(settings.LibraryId).ToString("N");
                var userId = Guid.Parse(settings.WatchedUserId).ToString("N");
                if (!GetUsers(settings).Any(user => Guid.Parse(user.Id).ToString("N") == userId) ||
                    !_jellyfinProxy.GetLibraries(settings).Any(library => Guid.Parse(library.ItemId).ToString("N") == libraryId))
                {
                    throw new JellyfinException("WatchedUserId", "The selected Jellyfin user or library is no longer accessible. Previous watched state is retained.");
                }

                // User-scoped library access must be confirmed before an empty collection
                // can be accepted. This never changes Jellyfin's playback state.
                var access = _httpClient.Get<JellyfinItem>(JellyfinProxy.BuildRequest($"Users/{userId}/Items/{libraryId}", settings).Build());
                JellyfinProxy.EnsureSuccess(access);
                if (access.StatusCode != HttpStatusCode.OK || !ValidId(access.Resource?.Id) ||
                    Guid.Parse(access.Resource.Id).ToString("N") != libraryId)
                {
                    throw InvalidSnapshot();
                }

                var items = new List<JellyfinWatchedItem>();
                var identifiers = new HashSet<Guid>();
                int? total = null;
                while (true)
                {
                    var request = JellyfinProxy.BuildRequest($"Users/{userId}/Items", settings)
                        .AddQueryParam("parentId", libraryId)
                        .AddQueryParam("recursive", "true")
                        .AddQueryParam("includeItemTypes", "Episode,Movie")
                        .AddQueryParam("fields", "Path")
                        .AddQueryParam("enableUserData", "true")
                        .AddQueryParam("enableTotalRecordCount", "true")
                        .AddQueryParam("sortBy", "SortName")
                        .AddQueryParam("sortOrder", "Ascending")
                        .AddQueryParam("startIndex", items.Count)
                        .AddQueryParam("limit", PageSize)
                        .Build();
                    var response = _httpClient.Get<JellyfinWatchedItems>(request);
                    JellyfinProxy.EnsureSuccess(response);
                    var page = response.Resource;
                    if (response.StatusCode != HttpStatusCode.OK || page?.Items == null ||
                        !page.TotalRecordCount.HasValue || page.TotalRecordCount < 0 ||
                        page.TotalRecordCount > MaximumItems || page.Items.Count > PageSize ||
                        (total.HasValue && total != page.TotalRecordCount) ||
                        items.Count + page.Items.Count > page.TotalRecordCount ||
                        page.Items.Any(item => item == null || !ValidId(item.Id) ||
                            !identifiers.Add(Guid.Parse(item.Id)) || JellyfinWatchPath.Normalize(item.Path) == null ||
                            item.Type is not ("Episode" or "Movie") || item.UserData?.Played.HasValue != true))
                    {
                        throw InvalidSnapshot();
                    }

                    total = page.TotalRecordCount;
                    items.AddRange(page.Items);
                    if (items.Count == total)
                    {
                        return items;
                    }

                    if (page.Items.Count == 0)
                    {
                        throw InvalidSnapshot();
                    }
                }
            }
            catch (JellyfinException)
            {
                throw;
            }
            catch (Exception)
            {
                // HttpResponse messages can contain request credentials and response data.
                throw new JellyfinException("Host", "Unable to read a complete Jellyfin watched snapshot. Previous watched state is retained and the next tick retries.");
            }
        }

        private static bool ValidId(string id)
        {
            return Guid.TryParse(id, out var value) && value != Guid.Empty;
        }

        private static JellyfinException InvalidSnapshot()
        {
            return new JellyfinException("Host", "Jellyfin returned an incomplete or invalid watched snapshot. Previous watched state is retained and the next tick retries.");
        }
    }
}
