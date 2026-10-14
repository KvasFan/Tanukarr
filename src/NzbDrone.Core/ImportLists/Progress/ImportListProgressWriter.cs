using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Progress
{
    public interface IImportListProgressWriter
    {
        bool TryGetTarget(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget target);
        int Advance(ImportListDefinition definition, ImportListProgressTarget target, int progress);
    }

    public class ImportListProgressTarget
    {
        public int MediaId { get; set; }
        public string Fingerprint { get; set; }
        public List<Episode> NativeEpisodes { get; set; }
        public int AcknowledgedProgress { get; set; }
    }

    public class ImportListProgressUnavailableException : Exception
    {
        public ImportListProgressUnavailableException()
            : base("Watched progress requires a valid own-account token, an existing list entry and matching native metadata.")
        {
        }
    }

    public static class ImportListProgressGuard
    {
        public static bool NativeIdentity(Series series, MetadataSourceType source, int mediaId)
        {
            return mediaId > 0 && series.MetadataSource == source &&
                   series.ForeignId == mediaId.ToString(CultureInfo.InvariantCulture) &&
                   string.IsNullOrWhiteSpace(series.OrderingId);
        }

        public static string Fingerprint(params string[] components)
        {
            // Length-prefix components so neither delimiter collisions nor secrets
            // are retained in the persistent queue or log output.
            var value = string.Concat(components.Select(component => (component?.Length ?? 0) + ":" + component));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        public static void OfficialRoot(string configured, string expected)
        {
            if (!Uri.TryCreate(configured?.Trim(), UriKind.Absolute, out var root) ||
                root.Scheme != Uri.UriSchemeHttps || root.UserInfo.Length != 0 || root.Query.Length != 0 ||
                root.Fragment.Length != 0 || !string.Equals(root.AbsoluteUri.TrimEnd('/'), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new ImportListProgressUnavailableException();
            }
        }

        public static HttpRequest PrivateRequest(HttpRequest request, string accessToken)
        {
            request.Headers["Authorization"] = "Bearer " + accessToken;
            request.AllowAutoRedirect = false;
            request.SuppressHttpError = true;
            request.LogHttpError = false;
            request.LogResponseContent = false;
            request.ContentSummary = null;
            request.RequestTimeout = TimeSpan.FromSeconds(30);
            return request;
        }

        public static JObject ObjectResponse(HttpResponse response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new TooManyRequestsException(response.Request, response);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListProgressUnavailableException();
            }

            return JObject.Parse(response.Content);
        }

        public static int Integer(JToken value)
        {
            if (value?.Type != JTokenType.Integer || !int.TryParse((string)value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
            {
                throw new ImportListProgressUnavailableException();
            }

            return result;
        }

        public static int ContiguousProgress(IEnumerable<Episode> episodes, IReadOnlyCollection<int> watchedEpisodeIds, bool multipleSeasons = false)
        {
            var native = episodes.Where(episode => episode.SeasonNumber > 0).OrderBy(episode => episode.SeasonNumber).ThenBy(episode => episode.EpisodeNumber).ToList();
            if (native.Count == 0 || native.Any(episode => (!multipleSeasons && episode.SeasonNumber != 1) || episode.EpisodeNumber <= 0) ||
                native.GroupBy(episode => (episode.SeasonNumber, episode.EpisodeNumber)).Any(group => group.Count() != 1))
            {
                return 0;
            }

            var watched = watchedEpisodeIds.ToHashSet();
            var progress = 0;
            var season = 1;
            var number = 1;
            foreach (var episode in native)
            {
                if (multipleSeasons && episode.SeasonNumber == season + 1)
                {
                    season++;
                    number = 1;
                }

                // Never count a missing native episode or an unwatched gap. Group
                // folder season numbers, specials and scene numbers are irrelevant.
                if (episode.SeasonNumber != season || episode.EpisodeNumber != number || !watched.Contains(episode.Id))
                {
                    break;
                }

                progress++;
                number++;
            }

            return progress;
        }
    }
}
