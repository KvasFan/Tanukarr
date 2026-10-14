using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Simkl
{
    public interface ISimklCustomListReader
    {
        SimklCustomListResult Read(SimklUserSettings settings);
        List<FieldSelectStringOption> GetOptions(SimklUserSettings settings);
        void Commit(SimklUserSettings settings, SimklCustomListResult result);
    }

    public class SimklCustomListReader : ISimklCustomListReader
    {
        private const int PageLimit = 500;
        private readonly ISimklV2RequestService _requests;
        private readonly ISimklV2WatchlistService _watchlists;

        public SimklCustomListReader(ISimklV2RequestService requests, ISimklV2WatchlistService watchlists)
        {
            _requests = requests;
            _watchlists = watchlists;
        }

        public SimklCustomListResult Read(SimklUserSettings settings)
        {
            var includedIds = ParseIds(settings.IncludedCustomListIds);
            var excludedIds = ParseIds(settings.ExcludedCustomListIds);
            var included = new HashSet<int>();
            var excluded = new HashSet<int>();
            var result = new SimklCustomListResult { WatchlistSnapshot = _watchlists.Read(settings) };
            foreach (var listId in includedIds.Union(excludedIds))
            {
                var membership = ReadList(settings, listId);
                if (includedIds.Contains(listId))
                {
                    included.UnionWith(membership);
                }

                if (excludedIds.Contains(listId))
                {
                    excluded.UnionWith(membership);
                }
            }

            var type = (SimklUserShowType)settings.ShowType;
            var rows = result.WatchlistSnapshot.Rows;
            if (rows == null)
            {
                throw Failure("Simkl did not return the selected account Watchlist bucket.");
            }

            foreach (var row in rows)
            {
                if (row?.Show?.Ids?.Simkl is not > 0 || row.Show.Title.IsNullOrWhiteSpace())
                {
                    throw Failure("Simkl returned a Watchlist entry without its native identity.");
                }

                if (type == SimklUserShowType.Anime && row.AnimeType is not (SimklAnimeType.Tv or SimklAnimeType.Ona or SimklAnimeType.Ova or SimklAnimeType.Special))
                {
                    continue;
                }

                if (row.Status != ((SimklUserListType)settings.ListType).ToString().ToLowerInvariant())
                {
                    continue;
                }

                var id = row.Show.Ids.Simkl;
                if ((includedIds.Count > 0 && !included.Contains(id)) || excluded.Contains(id))
                {
                    continue;
                }

                var ids = row.Show.Ids;
                var item = new ImportListItemInfo
                {
                    Title = row.Show.Title,
                    TvdbId = PositiveId(ids.Tvdb),
                    TmdbId = PositiveId(ids.Tmdb),
                    MalId = PositiveId(ids.Mal),
                    ImdbId = ids.Imdb
                };
                if (item.TvdbId > 0 || item.TmdbId > 0 || item.MalId > 0 || item.ImdbId.IsNotNullOrWhiteSpace())
                {
                    result.Items.Add(item);
                }
            }

            return result;
        }

        public void Commit(SimklUserSettings settings, SimklCustomListResult result)
        {
            _watchlists.Commit(settings, result.WatchlistSnapshot);
        }

        public List<FieldSelectStringOption> GetOptions(SimklUserSettings settings)
        {
            if (!int.TryParse(settings.AuthUser, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            {
                throw Failure("Authenticate with your AUTH V2 app before loading custom lists.");
            }

            var result = new List<FieldSelectStringOption>();
            var seen = new HashSet<int>();
            var expectedType = settings.ShowType == (int)SimklUserShowType.Anime ? "anime" : "tv";
            SimklCustomPagination first = null;
            for (var page = 1; page <= 20; page++)
            {
                var resource = ReadResource<SimklUserCustomLists>(settings, $"lists/user/{userId}?limit={PageLimit}&page={page}");
                ValidatePaging(resource.Pagination, page, ref first);
                if (resource.Lists == null || resource.Lists.Count > PageLimit)
                {
                    throw Failure("Simkl did not return custom list names.");
                }

                foreach (var list in resource.Lists)
                {
                    if (list?.Id is not > 0 || list.Name.IsNullOrWhiteSpace() || list.User?.Id != userId || !seen.Add(list.Id))
                    {
                        throw Failure("Simkl returned an invalid custom list owner or identity.");
                    }

                    if (list.MediaType == expectedType)
                    {
                        result.Add(new FieldSelectStringOption { Value = list.Id.ToString(CultureInfo.InvariantCulture), Name = list.Name });
                    }
                }

                if (page >= first.TotalPages)
                {
                    if (seen.Count != first.TotalItems)
                    {
                        throw Failure("Simkl returned only part of the custom list names.");
                    }

                    return result.DistinctBy(option => option.Value).ToList();
                }
            }

            throw Failure("Simkl custom list names exceeded the documented pagination limit.");
        }

        private HashSet<int> ReadList(SimklUserSettings settings, int id)
        {
            var key = ImportListSnapshotScope.Fingerprint(
                "SimklCustomMembership-v1",
                settings.BaseUrl,
                settings.ClientIdV2,
                settings.AuthUser,
                settings.AccessToken,
                settings.ScopeV2,
                settings.ShowType.ToString(CultureInfo.InvariantCulture),
                id.ToString(CultureInfo.InvariantCulture));
            return ImportListSnapshotScope.Current?.GetOrLoad(key, () => ReadListSnapshot(settings, id)) ?? ReadListSnapshot(settings, id);
        }

        private HashSet<int> ReadListSnapshot(SimklUserSettings settings, int id)
        {
            var result = new HashSet<int>();
            var expectedType = settings.ShowType == (int)SimklUserShowType.Anime ? "anime" : "tv";
            SimklCustomPagination first = null;
            string name = null;
            var ownerId = 0;
            long payloadBytes = 0;
            for (var page = 1; page <= 20; page++)
            {
                var resource = ReadResource<SimklCustomListPage>(settings, $"lists/{id}?limit={PageLimit}&page={page}", ref payloadBytes);
                ValidatePaging(resource.Pagination, page, ref first);
                if (resource.Id != id || resource.MediaType != expectedType || resource.Name.IsNullOrWhiteSpace() ||
                    resource.User?.Id is not > 0 || resource.Items == null || resource.Items.Count > PageLimit || resource.Counts?.Items != first.TotalItems)
                {
                    throw Failure("Simkl returned incomplete custom list identity, media type or counts.");
                }

                if (name != null && (name != resource.Name || ownerId != resource.User.Id))
                {
                    throw Failure("The Simkl custom list changed while its pages were being read.");
                }

                name = resource.Name;
                ownerId = resource.User.Id;
                foreach (var item in resource.Items)
                {
                    if (item?.Ids?.SimklId is not > 0 || item.Type != expectedType || !result.Add(item.Ids.SimklId))
                    {
                        throw Failure("Simkl returned an invalid or repeated native custom-list member.");
                    }
                }

                if (page >= first.TotalPages)
                {
                    if (result.Count != first.TotalItems)
                    {
                        throw Failure("Simkl returned only part of the selected custom list.");
                    }

                    return result;
                }
            }

            throw Failure("The selected Simkl custom list exceeded its documented pagination limit.");
        }

        private T ReadResource<T>(SimklUserSettings settings, string resource)
            where T : new()
        {
            long payloadBytes = 0;
            return ReadResource<T>(settings, resource, ref payloadBytes);
        }

        private T ReadResource<T>(SimklUserSettings settings, string resource, ref long payloadBytes)
            where T : new()
        {
            var request = SimklV2RequestBuilder.Build(resource, settings);
            var response = _requests.Execute(request);
            ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
            try
            {
                var json = JObject.Parse(response.Content);
                if (response.StatusCode != HttpStatusCode.OK || (json["error"] != null && json["error"].Type != JTokenType.Null))
                {
                    throw Failure("Simkl could not return this list. Custom lists require AUTH V2 and a PRO or VIP account, with permission to read every selected list.");
                }

                return Json.Deserialize<T>(response.Content);
            }
            catch (JsonException)
            {
                throw Failure("Simkl returned malformed list data.");
            }
        }

        private static void ValidatePaging(SimklCustomPagination paging, int page, ref SimklCustomPagination first)
        {
            if (paging == null || paging.Page != page || paging.Limit != PageLimit || !paging.TotalItems.HasValue || !paging.TotalPages.HasValue ||
                paging.TotalItems < 0 || paging.TotalItems > 10000 || paging.TotalPages < 0 || paging.TotalPages > 20 ||
                (paging.TotalItems == 0 ? paging.TotalPages > 1 : paging.TotalPages != (paging.TotalItems + PageLimit - 1) / PageLimit))
            {
                throw Failure("Simkl returned incomplete or unsafe custom-list pagination.");
            }

            if (first != null && (first.TotalItems != paging.TotalItems || first.TotalPages != paging.TotalPages))
            {
                throw Failure("The Simkl custom list changed while its pages were being read.");
            }

            first ??= paging;
        }

        private static HashSet<int> ParseIds(IEnumerable<string> ids) => (ids ?? Array.Empty<string>()).Select(id => int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture)).ToHashSet();
        private static int PositiveId(string id) => int.TryParse(id, out var value) && value > 0 ? value : 0;
        private static ImportListException Failure(string message) => new ImportListException(null, message + " The previous complete list will be retained.");
    }

    public class SimklCustomListResult
    {
        public List<ImportListItemInfo> Items { get; set; } = new();
        public SimklV2WatchlistSnapshot WatchlistSnapshot { get; set; }
    }

    public class SimklCustomPagination
    {
        public int Page { get; set; }
        public int Limit { get; set; }
        [JsonProperty("total_items")]
        public int? TotalItems { get; set; }
        [JsonProperty("total_pages")]
        public int? TotalPages { get; set; }
    }

    public class SimklCustomListPage
    {
        public int Id { get; set; }
        public string Name { get; set; }
        [JsonProperty("media_type")]
        public string MediaType { get; set; }
        public string Type { get; set; }
        public SimklCustomOwner User { get; set; }
        public SimklCustomCounts Counts { get; set; }
        public SimklCustomPagination Pagination { get; set; }
        public List<SimklCustomItem> Items { get; set; }
    }

    public class SimklCustomOwner
    {
        public int Id { get; set; }
    }

    public class SimklCustomCounts
    {
        public int? Items { get; set; }
    }

    public class SimklCustomItem
    {
        public string Type { get; set; }
        public SimklCustomIds Ids { get; set; }
    }

    public class SimklCustomIds
    {
        [JsonProperty("simkl_id")]
        public int SimklId { get; set; }
    }

    public class SimklUserCustomLists
    {
        public SimklCustomPagination Pagination { get; set; }
        public List<SimklCustomListPage> Lists { get; set; }
    }
}
