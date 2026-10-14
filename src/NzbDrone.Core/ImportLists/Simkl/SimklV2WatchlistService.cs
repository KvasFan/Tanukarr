using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.Simkl.User;

namespace NzbDrone.Core.ImportLists.Simkl
{
    public interface ISimklV2WatchlistService
    {
        SimklV2WatchlistSnapshot Read(SimklUserSettings settings);
        void Commit(SimklUserSettings settings, SimklV2WatchlistSnapshot snapshot);
    }

    public class SimklV2WatchlistService : ISimklV2WatchlistService
    {
        private static readonly string[] Statuses = { "watching", "plantowatch", "hold", "completed", "dropped" };
        private readonly ISimklV2RequestService _requests;
        private readonly IAppFolderInfo _folders;
        private readonly IDiskProvider _disk;
        private readonly ICached<object> _locks;

        public SimklV2WatchlistService(ISimklV2RequestService requests, IAppFolderInfo folders, IDiskProvider disk, ICacheManager cacheManager)
        {
            _requests = requests;
            _folders = folders;
            _disk = disk;
            _locks = cacheManager.GetCache<object>(typeof(SimklV2WatchlistService));
        }

        public SimklV2WatchlistSnapshot Read(SimklUserSettings settings)
        {
            var key = ImportListSnapshotScope.Fingerprint("SimklInventory-v1", Scope(settings), settings.AccessToken, settings.ScopeV2);
            return ImportListSnapshotScope.Current?.GetOrLoad(key, () => ReadInventory(settings)) ?? ReadInventory(settings);
        }

        private SimklV2WatchlistSnapshot ReadInventory(SimklUserSettings settings)
        {
            var scope = Scope(settings);
            lock (_locks.Get(scope, () => new object()))
            {
                var previous = Load(scope);
                long payloadBytes = 0;
                var activities = ReadObject(SimklV2RequestBuilder.Build("sync/activities", settings), ref payloadBytes);
                var bucket = settings.ShowType == (int)SimklUserShowType.Anime ? "anime" : "tv_shows";
                ValidateActivities(activities, bucket);
                if (previous != null)
                {
                    ValidateActivities(previous.Activities, bucket);
                    if (previous.Activities["all"].Type == JTokenType.String &&
                        (activities["all"].Type == JTokenType.Null ||
                            DateTimeOffset.Parse((string)activities["all"], CultureInfo.InvariantCulture) < DateTimeOffset.Parse((string)previous.Activities["all"], CultureInfo.InvariantCulture)))
                    {
                        throw Failure("Simkl returned an older activity cursor.");
                    }
                }

                var rows = previous?.Rows.ToDictionary(row => row.Show.Ids.Simkl) ?? new Dictionary<int, SimklSeriesResource>();
                var type = settings.ShowType == (int)SimklUserShowType.Anime ? "anime" : "shows";
                var oldActivities = previous?.Activities;
                var changed = previous == null || !JToken.DeepEquals(oldActivities["all"], activities["all"]);
                if (changed && (previous == null || Statuses.Any(status => !JToken.DeepEquals(oldActivities[bucket][status], activities[bucket][status]))))
                {
                    var request = SimklV2RequestBuilder.Build("sync/all-items/" + type, settings);
                    var since = (string)oldActivities?["all"];
                    if (since != null)
                    {
                        request.Url = request.Url.AddQueryParam("date_from", since);
                    }

                    foreach (var row in ReadRows(ReadObject(request, ref payloadBytes), type, false))
                    {
                        rows[row.Show.Ids.Simkl] = row;
                    }
                }

                if (previous != null && changed && !JToken.DeepEquals(oldActivities[bucket]["removed_from_list"], activities[bucket]["removed_from_list"]))
                {
                    var request = SimklV2RequestBuilder.Build("sync/all-items/" + type, settings);
                    request.Url = request.Url.AddQueryParam("extended", "simkl_ids_only");
                    var current = ReadRows(ReadObject(request, ref payloadBytes), type, true).Select(row => row.Show.Ids.Simkl).ToHashSet();
                    foreach (var id in rows.Keys.Where(id => !current.Contains(id)).ToArray())
                    {
                        rows.Remove(id);
                    }
                }

                var snapshot = new SimklV2WatchlistSnapshot
                {
                    Scope = scope,
                    BaselineRevision = previous?.Revision,
                    Activities = activities,
                    Rows = rows.Values.OrderBy(row => row.Show.Ids.Simkl).ToList()
                };
                long inventoryBytes = 0;
                ImportListSnapshotScope.AddPayloadBytes(ref inventoryBytes, Json.ToJson(snapshot));
                return snapshot;
            }
        }

        public void Commit(SimklUserSettings settings, SimklV2WatchlistSnapshot snapshot)
        {
            var scope = Scope(settings);
            if (snapshot?.Scope != scope)
            {
                throw Failure("The staged Simkl inventory belongs to another account, client or media type.");
            }

            lock (_locks.Get(scope, () => new object()))
            {
                var previous = Load(scope);
                if (snapshot.CommittedRevision != null)
                {
                    if (previous?.Revision != snapshot.CommittedRevision)
                    {
                        throw Failure("Another sync replaced the staged Simkl inventory after its commit.");
                    }

                    return;
                }

                if (previous?.Revision != snapshot.BaselineRevision)
                {
                    throw Failure("Another sync committed newer Simkl inventory. Retry using its saved cursor.");
                }

                var folder = Path.Combine(_folders.AppDataFolder, "cache", "simkl-v2");
                Directory.CreateDirectory(folder);
                if (OperatingSystem.IsWindows())
                {
                    _disk.SetCurrentUserPermissions(folder);
                    _disk.SetServiceAccountPermissions(folder);
                    _disk.RemoveEveryonePermissions(folder);
                }
                else
                {
                    File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                var path = SnapshotPath(scope);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var stored = new SimklV2WatchlistSnapshot
                {
                    Scope = scope,
                    Revision = Guid.NewGuid().ToString("N"),
                    Activities = snapshot.Activities,
                    Rows = snapshot.Rows
                };
                try
                {
                    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                    if (!OperatingSystem.IsWindows())
                    {
                        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    }

                    using (var stream = new FileStream(temporary, options))
                    {
                        var bytes = Encoding.UTF8.GetBytes(Json.ToJson(stored));
                        stream.Write(bytes);
                        stream.Flush(true);
                    }

                    File.Move(temporary, path, true);
                    snapshot.CommittedRevision = stored.Revision;
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }

        private SimklV2WatchlistSnapshot Load(string scope)
        {
            var path = SnapshotPath(scope);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                if (new FileInfo(path).Length > ImportListSnapshotScope.MaxSnapshotBytes)
                {
                    throw Failure("The saved Simkl inventory exceeds the response safety limit.");
                }

                var stored = JsonConvert.DeserializeObject<SimklV2WatchlistSnapshot>(File.ReadAllText(path), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                if (stored?.Scope != scope || string.IsNullOrEmpty(stored.Revision) || stored.Activities == null || stored.Rows == null ||
                    stored.Rows.Any(row => row?.Show?.Ids?.Simkl is not > 0 || !Statuses.Contains(row.Status, StringComparer.Ordinal)) ||
                    stored.Rows.Select(row => row.Show.Ids.Simkl).Distinct().Count() != stored.Rows.Count)
                {
                    throw Failure("The saved Simkl inventory is incomplete.");
                }

                return stored;
            }
            catch (JsonException)
            {
                throw Failure("The saved Simkl inventory is unreadable.");
            }
        }

        private JObject ReadObject(NzbDrone.Common.Http.HttpRequest request, ref long payloadBytes)
        {
            var response = _requests.Execute(request);
            ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
            try
            {
                using var reader = new JsonTextReader(new StringReader(response.Content)) { DateParseHandling = DateParseHandling.None };
                var body = JObject.Load(reader);
                if (response.StatusCode != HttpStatusCode.OK || body["error"] != null)
                {
                    throw Failure("Simkl could not return complete account inventory.");
                }

                return body;
            }
            catch (JsonException)
            {
                throw Failure("Simkl returned malformed account inventory.");
            }
        }

        private static List<SimklSeriesResource> ReadRows(JObject document, string type, bool identitiesOnly)
        {
            if (document.Count == 0)
            {
                return new List<SimklSeriesResource>();
            }

            if (document[type] is not JArray items || document.Properties().Any(property => property.Name != type &&
                (property.Name is not ("shows" or "anime" or "movies") || property.Value is not JArray other || other.Count > 0)))
            {
                throw Failure("Simkl omitted or changed the requested Watchlist media bucket.");
            }

            var result = new List<SimklSeriesResource>();
            var seen = new HashSet<int>();
            foreach (var item in items)
            {
                var id = item["show"]?["ids"]?["simkl"];
                if (id?.Type != JTokenType.Integer || !int.TryParse((string)id, NumberStyles.None, CultureInfo.InvariantCulture, out var nativeId) || nativeId <= 0 || !seen.Add(nativeId))
                {
                    throw Failure("Simkl returned an unidentified or repeated Watchlist item.");
                }

                if (identitiesOnly)
                {
                    result.Add(new SimklSeriesResource { Show = new SimklSeriesPropsResource { Ids = new SimklSeriesIdsResource { Simkl = nativeId } } });
                    continue;
                }

                var row = Json.Deserialize<SimklSeriesResource>(item.ToString());
                if (!Statuses.Contains(row.Status, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(row.Show.Title))
                {
                    throw Failure("Simkl returned an item without its current Watchlist status or title.");
                }

                result.Add(row);
            }

            return result;
        }

        private static void ValidateActivities(JObject activities, string bucket)
        {
            if (!activities.TryGetValue("all", out var all) || !Timestamp(all) || activities[bucket] is not JObject block ||
                Statuses.Concat(new[] { "removed_from_list" }).Any(name => !block.TryGetValue(name, out var value) || !Timestamp(value)))
            {
                throw Failure("Simkl did not return the activity stamps needed for incremental inventory sync.");
            }
        }

        private static bool Timestamp(JToken value)
        {
            return value.Type == JTokenType.Null || (value.Type == JTokenType.String &&
                (((string)value).EndsWith("Z", StringComparison.Ordinal) || ((string)value).EndsWith("+00:00", StringComparison.Ordinal)) &&
                DateTimeOffset.TryParse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        }

        private static string Scope(SimklUserSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ClientIdV2) || !int.TryParse(settings.AuthUser, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId) || accountId <= 0 ||
                !Enum.IsDefined(typeof(SimklUserShowType), settings.ShowType))
            {
                throw Failure("Authenticate with the registered Simkl AUTH V2 client before syncing its account inventory.");
            }

            var identity = JsonConvert.SerializeObject(new { client = settings.ClientIdV2, accountId, media = settings.ShowType, api = settings.BaseUrl });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }

        private string SnapshotPath(string scope) => Path.Combine(_folders.AppDataFolder, "cache", "simkl-v2", scope + ".json");
        private static ImportListException Failure(string message) => new(null, message + " The previous complete list will be retained.");
    }

    public class SimklV2WatchlistSnapshot
    {
        public string Scope { get; set; }
        public string Revision { get; set; }
        [JsonIgnore]
        public string BaselineRevision { get; set; }
        [JsonIgnore]
        internal string CommittedRevision { get; set; }
        public JObject Activities { get; set; }
        public List<SimklSeriesResource> Rows { get; set; }
    }
}
