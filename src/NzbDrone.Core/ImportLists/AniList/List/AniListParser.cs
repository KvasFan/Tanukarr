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

        public ISet<int> ExcludedMediaIds { get; } = new HashSet<int>();

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

            if (HasExclusions() && page.MediaList.Any(item => !IsKnownListStatus(item.Status)))
            {
                throw new ImportListException(importListResponse, "AniList returned an entry without a valid list status needed for exclusions. The previous list will be retained.");
            }

            if (_settings.ExcludedCustomLists?.Any() == true && page.MediaList.Any(item => item.CustomLists == null))
            {
                throw new ImportListException(importListResponse, "AniList returned an entry without custom list membership needed for exclusions. The previous list will be retained.");
            }

            foreach (var item in page.MediaList)
            {
                var media = item.Media;

                // Process every exclusion before inclusion filters so an excluded duplicate
                // cannot be admitted by a different status or custom list on another page.
                if (IsExcludedListStatus(item.Status) || IsOnExcludedCustomList(item, importListResponse))
                {
                    ExcludedMediaIds.Add(media.Id);
                    continue;
                }

                if (ExcludedMediaIds.Contains(media.Id) || !IsSelectedListStatus(item.Status) ||
                    !ValidateMediaStatus(media) || !IsOnSelectedCustomList(item))
                {
                    continue;
                }

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
            return result.Where(item => !ExcludedMediaIds.Contains(item.AniListId)).ToList();
        }

        /// <summary>
        /// Restricts the list to any of the user's selected custom lists, if named.
        /// The standard lists such as Planning and Watching cover everything the user has
        /// ever added, which is usually far more than they want Sonarr to manage.
        /// </summary>
        private bool IsOnSelectedCustomList(MediaList item)
        {
            var selectedNames = (_settings.IncludedCustomLists ?? Array.Empty<string>()).ToList();

            if (_settings.CustomList.IsNotNullOrWhiteSpace())
            {
                selectedNames.Add(_settings.CustomList.Trim());
            }

            if (selectedNames.Count == 0)
            {
                return true;
            }

            if (item.CustomLists == null)
            {
                return false;
            }

            return selectedNames.Any(name => IsCustomListMember(item.CustomLists, name, false, null));
        }

        private bool IsOnExcludedCustomList(MediaList item, ImportListResponse response)
        {
            var excludedNames = (_settings.ExcludedCustomLists ?? Array.Empty<string>()).ToList();

            if (excludedNames.Count == 0)
            {
                return false;
            }

            if (item.CustomLists == null)
            {
                throw new ImportListException(response, "AniList returned an entry without custom list membership needed for exclusions. The previous list will be retained.");
            }

            return excludedNames.Any(name => IsCustomListMember(item.CustomLists, name, true, response));
        }

        private static bool IsCustomListMember(Dictionary<string, bool> customLists, string name, bool exclude, ImportListResponse response)
        {
            if (name.IsNullOrWhiteSpace())
            {
                throw new ImportListException(response, "AniList custom list filters contain an empty name. Correct the settings before syncing. The previous list will be retained.");
            }

            if (customLists.TryGetValue(name, out var member))
            {
                return member;
            }

            // Keep manual entry forgiving without combining distinct lists that differ by case.
            var matchingLists = customLists.Where(l => l.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (exclude && matchingLists.Count > 1)
            {
                throw new ImportListException(response, "An AniList excluded custom list name is ambiguous by letter case. Use its exact name before syncing. The previous list will be retained.");
            }

            return matchingLists.Count == 1 && matchingLists[0].Value;
        }

        private bool IsSelectedListStatus(string status)
        {
            return status switch
            {
                MediaListStatus.Current => _settings.ImportCurrent,
                MediaListStatus.Planning => _settings.ImportPlanning,
                MediaListStatus.Completed => _settings.ImportCompleted,
                MediaListStatus.Dropped => _settings.ImportDropped,
                MediaListStatus.Paused => _settings.ImportPaused,
                MediaListStatus.Repeating => _settings.ImportRepeating,
                _ => false
            };
        }

        private static bool IsKnownListStatus(string status)
        {
            return status == MediaListStatus.Current || status == MediaListStatus.Planning ||
                   status == MediaListStatus.Completed || status == MediaListStatus.Dropped ||
                   status == MediaListStatus.Paused || status == MediaListStatus.Repeating;
        }

        private bool HasExclusions()
        {
            return _settings.ExcludedCustomLists?.Any() == true || _settings.ExcludeCurrent || _settings.ExcludePlanning ||
                   _settings.ExcludeCompleted || _settings.ExcludeDropped || _settings.ExcludePaused || _settings.ExcludeRepeating;
        }

        private bool IsExcludedListStatus(string status)
        {
            return status switch
            {
                MediaListStatus.Current => _settings.ExcludeCurrent,
                MediaListStatus.Planning => _settings.ExcludePlanning,
                MediaListStatus.Completed => _settings.ExcludeCompleted,
                MediaListStatus.Dropped => _settings.ExcludeDropped,
                MediaListStatus.Paused => _settings.ExcludePaused,
                MediaListStatus.Repeating => _settings.ExcludeRepeating,
                _ => false
            };
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
