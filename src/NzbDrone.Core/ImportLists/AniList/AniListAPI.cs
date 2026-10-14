using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.ImportLists.AniList
{
    public enum AniListQuery
    {
        None,
        QueryList,
        QueryMembership,
        QueryCollection
    }

    public static class AniListAPI
    {
        public const int PageSize = 50;
        public const int MaxPages = 5000 / PageSize;
        public const int CollectionChunkSize = 500;
        public const int MaxCollectionEntries = 11000;
        public const int MaxCollectionChunks = MaxCollectionEntries / CollectionChunkSize;

        private const string QueryCollection = @"
            query ($id: String!, $chunk: Int!) {
                User(name: $id) {
                    id name
                    mediaListOptions { animeList { customLists } }
                }
                MediaListCollection(userName: $id, type: ANIME, chunk: $chunk, perChunk: 500) {
                    user { id name }
                    hasNextChunk
                    lists {
                        name isCustomList
                        entries {
                            id userId status progress updatedAt
                            media {
                                id idMal type format status episodes
                                title { userPreferred romaji english native }
                                startDate { year month day }
                                endDate { year month day }
                                relations {
                                    edges {
                                        relationType(version: 2)
                                        node { id type format }
                                    }
                                }
                            }
                        }
                    }
                }
            }";

        public const string CustomListNamesQuery = @"
            query ($username: String!) {
                User(name: $username) {
                    mediaListOptions {
                        animeList { customLists }
                    }
                }
            }";

        private const string QueryMembership = @"
            query ($id: String!, $chunk: Int!) {
                User(name: $id) {
                    id
                    name
                    mediaListOptions { animeList { customLists } }
                }
                MediaListCollection(userName: $id, type: ANIME, chunk: $chunk, perChunk: 500) {
                    user { id name }
                    hasNextChunk
                    lists {
                        name
                        isCustomList
                        entries { media { id type } }
                    }
                }
            }";

        // The maximum items anilist will return is 50 per request.
        // Therefore, pagination must be used to retrieve the full list.
        private const string QueryList = @"
            query ($id: String!, $statusType: [MediaListStatus!]!, $page: Int!) {
                Page(page: $page, perPage: 50) {
                    pageInfo {
                        currentPage
                        hasNextPage
                    }
                    mediaList(userName: $id, type: ANIME, status_in: $statusType) {
                        status
                        progress
                        customLists
                        customListEntries: customLists(asArray: true)
                        media {
                            id
                            idMal
                            format
                            title {
                                userPreferred
                                romaji
                            }
                            status
                            episodes
                            startDate {
                                year
                                month
                                day
                            }
                            endDate {
                                year
                                month
                                day
                            }
                        }
                    }
                }
            }
        ";

        public static string BuildQuery(AniListQuery query, object data)
        {
            var querySource = "";
            if (query == AniListQuery.QueryList)
            {
                querySource = QueryList;
            }
            else if (query == AniListQuery.QueryMembership)
            {
                querySource = QueryMembership;
            }
            else if (query == AniListQuery.QueryCollection)
            {
                querySource = QueryCollection;
            }

            if (string.IsNullOrEmpty(querySource))
            {
                throw new Exception("Unknown Query Type");
            }

            var body = Json.ToJson(new
            {
                query = querySource,
                variables = data
            });
            return body;
        }
    }

    public class PageInfo
    {
        public int CurrentPage { get; set; }

        public int LastPage { get; set; }

        public bool? HasNextPage { get; set; }
    }

    public class MediaTitles
    {
        public string UserPreferred { get; set; }

        [JsonPropertyName("romaji")]
        public string UserRomaji { get; set; }

        public string English { get; set; }

        public string Native { get; set; }
    }

    public class MediaInfo
    {
        public int Id { get; set; }

        public string Type { get; set; }

        public AniListRelationConnection Relations { get; set; }

        public int? IdMal { get; set; }

        public string Status { get; set; }

        public string Format { get; set; }

        public int? Episodes { get; set; }

        public MediaTitles Title { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public YMDBlock StartDate { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public YMDBlock EndDate { get; set; }
    }

    public class MediaList
    {
        public MediaInfo Media { get; set; }

        public string Status { get; set; }

        public int Progress { get; set; }

        /// <summary>
        /// The user's own lists, keyed by name, with a flag for whether this entry belongs
        /// to each. AniList returns every list the user has defined on every entry, so
        /// absence and false both mean "not on that list".
        /// </summary>
        public Dictionary<string, bool> CustomLists { get; set; }

        // The documented array shape provides explicit membership when the map
        // is unavailable. Null is unknown; a complete empty array is known empty.
        public List<AniListCustomListEntry> CustomListEntries { get; set; }
    }

    public class AniListCustomListEntry
    {
        public string Name { get; set; }

        public bool? Enabled { get; set; }
    }

    public class MediaPage
    {
        public PageInfo PageInfo { get; set; }

        public List<MediaList> MediaList { get; set; }
    }

    public class MediaPageData
    {
        public MediaPage Page { get; set; }
    }

    public class MediaPageResponse
    {
        public MediaPageData Data { get; set; }

        public List<MediaPageError> Errors { get; set; }
    }

    public class MediaPageError
    {
        public string Message { get; set; }
    }

    public class YMDBlock
    {
        public int? Year { get; set; }

        public int? Month { get; set; }

        public int? Day { get; set; }

        public DateTime? Convert()
        {
            if (Year == null)
            {
                return null;
            }

            return new DateTime((int)Year, Month ?? 1, Day ?? 1);
        }
    }
}
