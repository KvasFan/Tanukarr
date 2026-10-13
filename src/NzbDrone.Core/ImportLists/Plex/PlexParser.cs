using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Notifications.Plex.PlexTv;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Plex
{
    public class PlexParser : IParseImportListResponse
    {
        private ImportListResponse _importResponse;

        public virtual IList<ImportListItemInfo> ParseResponse(ImportListResponse importResponse)
        {
            List<PlexWatchlistItem> items;

            _importResponse = importResponse;

            var series = new List<ImportListItemInfo>();

            if (!PreProcess(_importResponse))
            {
                return series;
            }

            items = Json.Deserialize<PlexResponse<PlexWatchlistRespone>>(_importResponse.Content)
                        .MediaContainer
                        .Items;

            foreach (var item in items)
            {
                series.Add(ParseItem(item));
            }

            return series;
        }

        public PlexWatchlistPage ParsePage(ImportListResponse response, int requestedOffset)
        {
            PreProcess(response);
            JObject body;
            try
            {
                body = JObject.Parse(response.Content);
            }
            catch (JsonException)
            {
                throw PaginationFailure(response);
            }

            if (body["MediaContainer"] is not JObject container || body["error"] != null || container["error"] != null)
            {
                throw PaginationFailure(response);
            }

            var offset = Counter(container["offset"], response);
            var size = Counter(container["size"], response);
            var bodyTotal = Counter(container["totalSize"], response);
            var headerOffset = HeaderCounter(response, "X-Plex-Container-Start");
            var headerTotal = HeaderCounter(response, "X-Plex-Container-Total-Size");
            if (offset != requestedOffset || !size.HasValue ||
                (headerOffset.HasValue && headerOffset != offset) ||
                (bodyTotal.HasValue && headerTotal.HasValue && bodyTotal != headerTotal))
            {
                throw PaginationFailure(response);
            }

            var rows = container["Metadata"] as JArray;
            if (rows == null && (size != 0 || container["Metadata"] != null))
            {
                throw PaginationFailure(response);
            }

            rows ??= new JArray();
            var total = bodyTotal ?? headerTotal;
            if (rows.Count != size || (total.HasValue && (long)requestedOffset + rows.Count > total))
            {
                throw PaginationFailure(response);
            }

            var series = new List<ImportListItemInfo>();
            foreach (var row in rows)
            {
                if (row is not JObject entry || entry["type"]?.Type != JTokenType.String)
                {
                    throw PaginationFailure(response);
                }

                // Advance by raw rows even when the service includes other media.
                if (!string.Equals((string)entry["type"], "show", StringComparison.Ordinal))
                {
                    continue;
                }

                var item = entry.ToObject<PlexWatchlistItem>();
                if (string.IsNullOrWhiteSpace(item.Title) || item.Guids == null || item.Guids.Any(guid => string.IsNullOrWhiteSpace(guid?.Id)))
                {
                    throw PaginationFailure(response);
                }

                var mapped = ParseItem(item);
                var validImdb = mapped.ImdbId?.Length > 2 && mapped.ImdbId.StartsWith("tt", StringComparison.Ordinal) &&
                    mapped.ImdbId.Skip(2).All(character => character >= '0' && character <= '9');
                if (mapped.TvdbId < 0 || mapped.TmdbId < 0 ||
                    (mapped.TvdbId <= 0 && mapped.TmdbId <= 0 && !validImdb))
                {
                    throw PaginationFailure(response);
                }

                series.Add(mapped);
            }

            return new PlexWatchlistPage { Offset = requestedOffset, Size = rows.Count, Total = total, Series = series };
        }

        private static int? Counter(JToken token, ImportListResponse response)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw PaginationFailure(response);
            }

            return count;
        }

        private static int? HeaderCounter(ImportListResponse response, string name)
        {
            var values = response.HttpResponse.Headers.GetValues(name);
            if (values == null)
            {
                return null;
            }

            if (values.Length != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw PaginationFailure(response);
            }

            return count;
        }

        private static ImportListException PaginationFailure(ImportListResponse response)
        {
            return new ImportListException(response, "Plex did not return complete, consistent Watchlist pagination. The previous list will be retained.");
        }

        protected virtual bool PreProcess(ImportListResponse importListResponse)
        {
            if (importListResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListException(importListResponse, "Plex API call resulted in an unexpected StatusCode [{0}]", importListResponse.HttpResponse.StatusCode);
            }

            if (importListResponse.HttpResponse.Headers.ContentType != null && importListResponse.HttpResponse.Headers.ContentType.Contains("text/json") &&
                importListResponse.HttpRequest.Headers.Accept != null && !importListResponse.HttpRequest.Headers.Accept.Contains("text/json"))
            {
                throw new ImportListException(importListResponse, "Plex API responded with html content. Site is likely blocked or unavailable.");
            }

            return true;
        }

        private static ImportListItemInfo ParseItem(PlexWatchlistItem item)
        {
            int.TryParse(FindGuid(item.Guids, "tvdb"), out var tvdbId);
            int.TryParse(FindGuid(item.Guids, "tmdb"), out var tmdbId);
            return new ImportListItemInfo
            {
                TvdbId = tvdbId,
                TmdbId = tmdbId,
                ImdbId = FindGuid(item.Guids, "imdb"),
                Title = item.Title,
                Year = item.Year
            };
        }

        private static string FindGuid(List<PlexSectionItemGuid> guids, string prefix)
        {
            var scheme = $"{prefix}://";

            return guids.FirstOrDefault((guid) => guid.Id.StartsWith(scheme))?.Id.Replace(scheme, "");
        }
    }

    public class PlexWatchlistPage
    {
        public int Offset { get; set; }
        public int Size { get; set; }
        public int? Total { get; set; }
        public IList<ImportListItemInfo> Series { get; set; }
    }
}
