using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    internal sealed class AniListAccountSnapshot
    {
        private const long MaxPayloadBytes = 8 * 1024 * 1024;
        private readonly Dictionary<int, AccountEntry> _entries = new();
        private readonly Dictionary<int, HashSet<string>> _membership = new();
        private HashSet<string> _definedNames;
        private bool _hasDeclaredNames;

        public long PayloadBytes { get; private set; }

        public IEnumerable<MediaInfo> PublicMedia => _entries.Values.Select(entry => entry.Media);

        public static AniListAccountSnapshot Load(string username, Func<int, ImportListResponse> fetch)
        {
            var snapshot = new AniListAccountSnapshot();
            var chunkFingerprints = new HashSet<string>(StringComparer.Ordinal);
            var ownerId = 0;

            for (var chunk = 1; chunk <= AniListAPI.MaxCollectionChunks; chunk++)
            {
                var response = fetch(chunk);
                snapshot.PayloadBytes += Encoding.UTF8.GetByteCount(response.Content);
                if (snapshot.PayloadBytes > MaxPayloadBytes)
                {
                    throw Failure("The account snapshot exceeded the temporary memory limit.");
                }

                AccountResponse resource;
                try
                {
                    resource = STJson.Deserialize<AccountResponse>(response.Content);
                }
                catch (System.Text.Json.JsonException)
                {
                    throw Failure();
                }

                var user = resource?.Data?.User;
                var collection = resource?.Data?.MediaListCollection;
                var names = user?.MediaListOptions?.AnimeList?.CustomLists;
                if (response.HttpResponse.StatusCode != HttpStatusCode.OK || resource?.Errors?.Count > 0 ||
                    user?.Id is not > 0 || user.Name.IsNullOrWhiteSpace() ||
                    !string.Equals(user.Name, username?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    collection?.User?.Id != user.Id ||
                    !string.Equals(collection.User.Name, user.Name, StringComparison.OrdinalIgnoreCase) ||
                    !collection.HasNextChunk.HasValue || collection.Lists == null ||
                    (names != null && (names.Count > 128 || names.Any(name => name.IsNullOrWhiteSpace()) ||
                     names.Distinct(StringComparer.Ordinal).Count() != names.Count)))
                {
                    throw Failure();
                }

                if (snapshot._definedNames == null)
                {
                    snapshot._definedNames = new HashSet<string>(names ?? new List<string>(), StringComparer.Ordinal);
                    snapshot._hasDeclaredNames = names != null;
                    ownerId = user.Id;
                }
                else if (ownerId != user.Id || snapshot._hasDeclaredNames != (names != null) ||
                    (snapshot._hasDeclaredNames && !snapshot._definedNames.SetEquals(names)))
                {
                    throw Failure();
                }

                if (collection.Lists.Count > 512 || !chunkFingerprints.Add(ImportListSnapshotScope.Fingerprint(Json.ToJson(collection.Lists))))
                {
                    throw Failure("AniList repeated a collection chunk or returned excessive list groups.");
                }

                var occurrences = 0;
                foreach (var group in collection.Lists)
                {
                    if (group == null || !group.IsCustomList.HasValue || group.Entries == null)
                    {
                        throw Failure();
                    }

                    if (group.IsCustomList == true)
                    {
                        if (group.Name.IsNullOrWhiteSpace() ||
                            (snapshot._hasDeclaredNames && !snapshot._definedNames.Contains(group.Name)))
                        {
                            throw Failure();
                        }

                        // Nullable options do not enumerate the user's custom names.
                        // Only a complete collection establishes them from all custom
                        // groups, including groups hidden from the default status lists.
                        snapshot._definedNames.Add(group.Name);
                        if (snapshot._definedNames.Count > 128)
                        {
                            throw Failure();
                        }
                    }

                    foreach (var entry in group.Entries)
                    {
                        occurrences++;
                        if (entry?.Id is not > 0 || entry.UserId != ownerId || entry.Media?.Id is not > 0 || entry.Media.Type != "ANIME" ||
                            !IsListStatus(entry.Status) || entry.Progress is < 0 || entry.UpdatedAt is < 0 ||
                            !IsMediaStatus(entry.Media.Status))
                        {
                            throw Failure();
                        }

                        if (snapshot._entries.TryGetValue(entry.Media.Id, out var existing))
                        {
                            if (existing.Id != entry.Id || existing.Status != entry.Status || existing.Progress != entry.Progress ||
                                existing.UpdatedAt != entry.UpdatedAt || Json.ToJson(existing.Media) != Json.ToJson(entry.Media))
                            {
                                throw Failure("AniList changed an entry while its collection was being fetched.");
                            }
                        }
                        else
                        {
                            snapshot._entries.Add(entry.Media.Id, entry);
                            snapshot._membership.Add(entry.Media.Id, new HashSet<string>(StringComparer.Ordinal));
                        }

                        if (group.IsCustomList == true)
                        {
                            snapshot._membership[entry.Media.Id].Add(group.Name);
                        }
                    }
                }

                // The provider silently truncates collections at 11,000 unique
                // entries. Without an authoritative total, reaching that boundary
                // cannot establish that the selected account was fetched completely.
                if (snapshot._entries.Count >= AniListAPI.MaxCollectionEntries)
                {
                    throw Failure("AniList's 11,000-entry collection boundary was reached.");
                }

                if (collection.HasNextChunk == false)
                {
                    return snapshot;
                }

                if (occurrences == 0)
                {
                    throw Failure("AniList returned an empty collection chunk with more data promised.");
                }
            }

            throw Failure("AniList did not finish its collection within the bounded chunk limit.");
        }

        public IEnumerable<MediaList> GetEntries()
        {
            foreach (var entry in _entries.Values)
            {
                var membership = _membership[entry.Media.Id];
                yield return new MediaList
                {
                    Media = entry.Media,
                    Status = entry.Status,
                    Progress = entry.Progress ?? 0,
                    CustomLists = _definedNames.ToDictionary(name => name, membership.Contains, StringComparer.Ordinal)
                };
            }
        }

        private static bool IsListStatus(string status)
        {
            return status is MediaListStatus.Current or MediaListStatus.Planning or MediaListStatus.Completed or
                MediaListStatus.Dropped or MediaListStatus.Paused or MediaListStatus.Repeating;
        }

        private static bool IsMediaStatus(string status)
        {
            return status is MediaStatus.Finished or MediaStatus.Releasing or MediaStatus.Unreleased or MediaStatus.Cancelled or MediaStatus.Hiatus;
        }

        private static ImportListException Failure(string detail = null)
        {
            // Do not retain/log a private account response or provider error body.
            return new ImportListException(null, "AniList could not confirm a complete account list. " +
                (detail == null ? string.Empty : detail + " ") + "The previous list will be retained; retry after checking the username and authentication.");
        }

        public class AccountResponse
        {
            public AccountData Data { get; set; }
            public List<MediaPageError> Errors { get; set; }
        }

        public class AccountData
        {
            public AniListCustomListMembershipSnapshot.MembershipUser User { get; set; }
            public AccountCollection MediaListCollection { get; set; }
        }

        public class AccountCollection
        {
            public AniListCustomListMembershipSnapshot.MembershipUser User { get; set; }
            public bool? HasNextChunk { get; set; }
            public List<AccountGroup> Lists { get; set; }
        }

        public class AccountGroup
        {
            public string Name { get; set; }
            public bool? IsCustomList { get; set; }
            public List<AccountEntry> Entries { get; set; }
        }

        public class AccountEntry
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string Status { get; set; }
            public int? Progress { get; set; }
            public int? UpdatedAt { get; set; }
            public MediaInfo Media { get; set; }
        }
    }
}
