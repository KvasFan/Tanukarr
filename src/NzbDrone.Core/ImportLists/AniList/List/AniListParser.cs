using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    public class AniListParser : IParseImportListResponse
    {
        private readonly AniListSettings _settings;

        public AniListParser(AniListSettings settings)
        {
            _settings = settings;
        }

        public virtual IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse)
        {
            return ParseResponse(importListResponse, out _);
        }

        public IList<ImportListItemInfo> ParseResponse(ImportListResponse importListResponse, out PageInfo pageInfo)
        {
            var result = new List<ImportListItemInfo>();

            if (!PreProcess(importListResponse))
            {
                pageInfo = null;
                return result;
            }

            var jsonResponse = STJson.Deserialize<MediaPageResponse>(importListResponse.Content);

            if (jsonResponse?.Errors?.Count > 0)
            {
                throw new ImportListException(importListResponse, "AniList returned GraphQL errors: {0}", string.Join("; ", jsonResponse.Errors.Select(e => e.Message)));
            }

            var page = jsonResponse?.Data?.Page;

            if (page?.MediaList == null || page.PageInfo == null || page.PageInfo.CurrentPage < 1 || !page.PageInfo.HasNextPage.HasValue)
            {
                throw new ImportListException(importListResponse, "AniList returned an incomplete list response. The previous list will be retained.");
            }

            if (page.MediaList.Any(item => item?.Media == null || item.Media.Id <= 0))
            {
                throw new ImportListException(importListResponse, "AniList returned a list entry without a valid media ID. The previous list will be retained.");
            }

            // Anilist currently does not support filtering this at the query level, they will get filtered out here.
            var filtered = page.MediaList
                .Where(x => ValidateMediaStatus(x.Media))
                .Where(IsOnSelectedCustomList);

            foreach (var item in filtered)
            {
                var media = item.Media;

                var entry = new ImportListItemInfo
                {
                    AniListId = media.Id,
                    MalId = media.IdMal ?? 0,
                    Year = media.StartDate?.Year ?? 0,
                    Title = media.Title?.UserPreferred.IsNotNullOrWhiteSpace() == true ? media.Title.UserPreferred : media.Title?.UserRomaji
                };

                result.Add(entry);
            }

            pageInfo = page.PageInfo;
            return result;
        }

        /// <summary>
        /// Restricts the list to one of the user's own custom lists, if they named one.
        /// The standard lists such as Planning and Watching cover everything the user has
        /// ever added, which is usually far more than they want Sonarr to manage.
        /// </summary>
        private bool IsOnSelectedCustomList(MediaList item)
        {
            if (_settings.CustomList.IsNullOrWhiteSpace())
            {
                return true;
            }

            if (item.CustomLists == null)
            {
                return false;
            }

            var name = _settings.CustomList.Trim();

            if (item.CustomLists.TryGetValue(name, out var member))
            {
                return member;
            }

            // Keep manual entry forgiving without combining distinct lists that differ by case.
            var matchingLists = item.CustomLists.Where(l => l.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            return matchingLists.Count == 1 && matchingLists[0].Value;
        }

        private bool ValidateMediaStatus(MediaInfo media)
        {
            if (media.Status == MediaStatus.Finished && _settings.ImportFinished)
            {
                return true;
            }

            if (media.Status == MediaStatus.Releasing && _settings.ImportReleasing)
            {
                return true;
            }

            if (media.Status == MediaStatus.Unreleased && _settings.ImportUnreleased)
            {
                return true;
            }

            if (media.Status == MediaStatus.Cancelled && _settings.ImportCancelled)
            {
                return true;
            }

            if (media.Status == MediaStatus.Hiatus && _settings.ImportHiatus)
            {
                return true;
            }

            return false;
        }

        protected virtual bool PreProcess(ImportListResponse netImportResponse)
        {
            if (netImportResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListException(netImportResponse, "Anilist API call resulted in an unexpected StatusCode [{0}]", netImportResponse.HttpResponse.StatusCode);
            }

            if (netImportResponse.HttpResponse.Headers.ContentType != null && !netImportResponse.HttpResponse.Headers.ContentType.Contains("text/json") &&
                netImportResponse.HttpRequest.Headers.Accept != null && netImportResponse.HttpRequest.Headers.Accept.Contains("text/json"))
            {
                throw new ImportListException(netImportResponse, "Anilist API responded with html content. Site is likely blocked or unavailable.");
            }

            return true;
        }
    }
}
