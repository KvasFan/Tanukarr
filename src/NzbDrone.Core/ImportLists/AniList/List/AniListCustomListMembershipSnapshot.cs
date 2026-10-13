using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    internal sealed class AniListCustomListMembershipSnapshot
    {
        private readonly Dictionary<int, Dictionary<string, bool>> _membership = new();

        public static AniListCustomListMembershipSnapshot Load(string username, Func<int, ImportListResponse> fetch)
        {
            var snapshot = new AniListCustomListMembershipSnapshot();
            HashSet<string> definedNames = null;
            var ownerId = 0;

            for (var chunk = 1; chunk <= AniListAPI.MaxPages; chunk++)
            {
                var response = fetch(chunk);
                MembershipResponse resource;
                try
                {
                    resource = STJson.Deserialize<MembershipResponse>(response.Content);
                }
                catch (System.Text.Json.JsonException)
                {
                    throw Failure(response);
                }

                var user = resource?.Data?.User;
                var collection = resource?.Data?.MediaListCollection;
                var names = user?.MediaListOptions?.AnimeList?.CustomLists;
                if (response.HttpResponse.StatusCode != HttpStatusCode.OK || resource?.Errors?.Count > 0 ||
                    user?.Id is not > 0 || user.Name.IsNullOrWhiteSpace() ||
                    !string.Equals(user.Name, username?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    collection?.User?.Id != user.Id ||
                    !string.Equals(collection.User.Name, user.Name, StringComparison.OrdinalIgnoreCase) ||
                    !collection.HasNextChunk.HasValue || collection.Lists == null || names == null ||
                    names.Any(name => name.IsNullOrWhiteSpace()) || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
                {
                    throw Failure(response);
                }

                if (definedNames == null)
                {
                    definedNames = new HashSet<string>(names, StringComparer.Ordinal);
                    ownerId = user.Id;
                }
                else if (ownerId != user.Id || !definedNames.SetEquals(names))
                {
                    throw Failure(response);
                }

                foreach (var list in collection.Lists)
                {
                    if (list == null || !list.IsCustomList.HasValue || list.Name.IsNullOrWhiteSpace() || list.Entries == null ||
                        (list.IsCustomList == true && !definedNames.Contains(list.Name)))
                    {
                        throw Failure(response);
                    }

                    foreach (var entry in list.Entries)
                    {
                        if (entry?.Media?.Id is not > 0 || entry.Media.Type != "ANIME")
                        {
                            throw Failure(response);
                        }

                        if (!snapshot._membership.TryGetValue(entry.Media.Id, out var flags))
                        {
                            flags = definedNames.ToDictionary(name => name, _ => false, StringComparer.Ordinal);
                            snapshot._membership.Add(entry.Media.Id, flags);
                        }

                        if (list.IsCustomList == true)
                        {
                            flags[list.Name] = true;
                        }
                    }
                }

                if (collection.HasNextChunk == false)
                {
                    return snapshot;
                }
            }

            throw Failure(null);
        }

        public Dictionary<string, bool> GetMembership(int mediaId)
        {
            if (!_membership.TryGetValue(mediaId, out var flags))
            {
                // Absence from a complete collection does not establish membership
                // for an entry returned by a different or changing list response.
                throw Failure(null);
            }

            return new Dictionary<string, bool>(flags, StringComparer.Ordinal);
        }

        private static ImportListException Failure(ImportListResponse response)
        {
            return new ImportListException(response,
                "AniList could not confirm complete custom list membership for this user. The previous list will be retained; retry after checking the username and authentication.");
        }

        public class MembershipResponse
        {
            public MembershipData Data { get; set; }
            public List<MediaPageError> Errors { get; set; }
        }

        public class MembershipData
        {
            public MembershipUser User { get; set; }
            public MembershipCollection MediaListCollection { get; set; }
        }

        public class MembershipUser
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public MembershipOptions MediaListOptions { get; set; }
        }

        public class MembershipOptions
        {
            public MembershipAnimeOptions AnimeList { get; set; }
        }

        public class MembershipAnimeOptions
        {
            public List<string> CustomLists { get; set; }
        }

        public class MembershipCollection
        {
            public MembershipUser User { get; set; }
            public bool? HasNextChunk { get; set; }
            public List<MembershipList> Lists { get; set; }
        }

        public class MembershipList
        {
            public string Name { get; set; }
            public bool? IsCustomList { get; set; }
            public List<MembershipEntry> Entries { get; set; }
        }

        public class MembershipEntry
        {
            public MembershipMedia Media { get; set; }
        }

        public class MembershipMedia
        {
            public int Id { get; set; }
            public string Type { get; set; }
        }
    }
}
