using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    public class MyAnimeListParser : IParseImportListResponse
    {
        private readonly MyAnimeListSettings _settings;

        public MyAnimeListParser(MyAnimeListSettings settings)
        {
            _settings = settings;
        }

        public ISet<int> ExcludedMediaIds { get; } = new HashSet<int>();

        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            return ParseResponse(importListResponse, out _, out _);
        }

        public IList<ImportListItemInfo> ParseResponse(ImportListResponse response, out string next, out int count)
        {
            var resource = ReadPage(response);
            next = resource.Paging.Next;
            count = resource.Animes.Count;
            return ParseValidatedItems(resource.Animes, response);
        }

        internal static MyAnimeListResponse ReadPage(ImportListResponse response)
        {
            MyAnimeListResponse resource;
            try
            {
                resource = Json.Deserialize<MyAnimeListResponse>(response.Content);
            }
            catch (JsonException)
            {
                throw Failure(response);
            }

            if (resource?.Animes == null || resource.Paging == null || resource.Animes.Count > 1000)
            {
                throw Failure(response);
            }

            foreach (var item in resource.Animes)
            {
                if (item?.AnimeListInfo?.Id is not > 0 || item.AnimeListInfo.Title.IsNullOrWhiteSpace() ||
                    item.ListStatus?.Score is not >= 0 or > 10 ||
                    !MyAnimeListRequestGenerator.StatusMapping.Values.Contains(item.ListStatus.Status))
                {
                    throw Failure(response);
                }
            }

            return resource;
        }

        internal IList<ImportListItemInfo> ParseValidatedItems(IEnumerable<MyAnimeListItem> items, ImportListResponse response = null)
        {
            _settings.NormalizeLegacyStatus();
            var selectedStatuses = _settings.ListStatuses.ToHashSet();
            var included = (_settings.IncludedTags ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var excluded = (_settings.ExcludedTags ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var result = new List<ImportListItemInfo>();

            foreach (var item in items)
            {
                var node = item?.AnimeListInfo;
                var status = item?.ListStatus;

                if ((included.Count > 0 || excluded.Count > 0) &&
                    (status.Tags == null || status.Tags.Any(tag => tag.IsNullOrWhiteSpace())))
                {
                    throw new ImportListException(response, "MyAnimeList did not return the user tags needed for these filters. The previous list will be retained.");
                }

                if (status.Tags?.Any(excluded.Contains) == true)
                {
                    ExcludedMediaIds.Add(node.Id);
                    continue;
                }

                // MAL's official contract categorizes an active rewatch as Watching.
                var accountStatus = status.IsRewatching ? "watching" : status.Status;
                var selectedStatus = selectedStatuses.Contains((int)MyAnimeListStatus.All) ||
                    MyAnimeListRequestGenerator.StatusMapping.Any(pair => pair.Value == accountStatus && selectedStatuses.Contains((int)pair.Key));
                if (!SelectedRelease(node.Status, response) || !selectedStatus || status.Score < _settings.MinimumScore ||
                    (included.Count > 0 && status.Tags?.Any(included.Contains) != true) || ExcludedMediaIds.Contains(node.Id))
                {
                    continue;
                }

                result.Add(new ImportListItemInfo { Title = node.Title, MalId = node.Id });
            }

            return result.Where(item => !ExcludedMediaIds.Contains(item.MalId)).ToList();
        }

        private bool SelectedRelease(string status, ImportListResponse response)
        {
            return status switch
            {
                "finished_airing" => _settings.ImportFinished,
                "currently_airing" => _settings.ImportAiring,
                "not_yet_aired" => _settings.ImportUnreleased,
                _ when _settings.ImportFinished && _settings.ImportAiring && _settings.ImportUnreleased => true,
                _ => throw new ImportListException(response, "MyAnimeList did not return the anime release status needed for these filters. The previous list will be retained.")
            };
        }

        private static ImportListException Failure(ImportListResponse response)
        {
            return new ImportListException(response, "MyAnimeList returned an incomplete or invalid account list. The previous list will be retained.");
        }
    }
}
