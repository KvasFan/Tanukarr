using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISeriesPresentationService
    {
        void Enrich(Series metadata, Series previous = null);
        SeriesPresentation GetCollectionPresentation(string groupId);
    }

    public class SeriesPresentationService : ISeriesPresentationService
    {
        private const int MaxAlternativeLookups = 2;
        private readonly ISeriesPresentationProvider[] _providers;
        private readonly SeriesPresentationCache _cache;
        private readonly Logger _logger;
        private readonly IConfigService _config;

        public SeriesPresentationService(IEnumerable<ISeriesPresentationProvider> providers, SeriesPresentationCache cache, IConfigService config, Logger logger)
        {
            _providers = providers.ToArray();
            _cache = cache;
            _logger = logger;
            _config = config;
        }

        public void Enrich(Series metadata, Series previous = null)
        {
            metadata.Images ??= new List<MediaCover.MediaCover>();

            // A temporarily empty provider response must not erase cached presentation.
            if (previous != null && SameIdentity(metadata, previous))
            {
                FillMissing(metadata, new SeriesPresentation { Overview = previous.Overview, Images = previous.Images });
            }

            var identity = VerifiedIdentity(metadata, previous != null && SameIdentity(metadata, previous) ? previous : null);
            var attempts = 0;
            foreach (var source in MetadataSourcePolicy.Enabled(_config))
            {
                if (source == metadata.MetadataSource || !NeedsPresentation(metadata, source) || attempts >= MaxAlternativeLookups)
                {
                    continue;
                }

                var provider = _providers.FirstOrDefault(candidate => candidate.Source == source);
                var key = PresentationKey(source, identity);
                if (provider == null || key == null)
                {
                    continue;
                }

                attempts++;
                var presentation = GetPresentation(provider, identity, key);
                FillMissing(metadata, presentation);
            }
        }

        public SeriesPresentation GetCollectionPresentation(string groupId)
        {
            var parts = groupId?.Split(':');
            if (parts?.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                id <= 0 || parts[1] != id.ToString(CultureInfo.InvariantCulture))
            {
                return null;
            }

            MetadataSourceType source;
            if (parts[0] == "anilist")
            {
                source = MetadataSourceType.AniList;
            }
            else if (parts[0] == "mal")
            {
                source = MetadataSourceType.MyAnimeList;
            }
            else
            {
                return null;
            }

            var identity = new Series
            {
                MetadataSource = source,
                ForeignId = parts[1],
                AniListIds = source == MetadataSourceType.AniList ? new HashSet<int> { id } : new HashSet<int>(),
                MalIds = source == MetadataSourceType.MyAnimeList ? new HashSet<int> { id } : new HashSet<int>()
            };
            if (MetadataSourcePolicy.Disabled(_config).Contains(source))
            {
                return null;
            }

            var provider = _providers.FirstOrDefault(candidate => candidate.Source == source);
            return provider == null ? null : GetPresentation(provider, identity, PresentationKey(source, identity));
        }

        private SeriesPresentation GetPresentation(ISeriesPresentationProvider provider, Series identity, string key)
        {
            return _cache.Get(key, () =>
            {
                if (_cache.IsProviderCoolingDown(provider.Source))
                {
                    return null;
                }

                try
                {
                    var result = provider.GetPresentation(identity);
                    if (result == null)
                    {
                        return null;
                    }

                    return new SeriesPresentation
                    {
                        Overview = result.Overview,
                        Images = result.Images?.Where(cover => cover != null &&
                            (cover.CoverType == MediaCoverTypes.Poster || cover.CoverType == MediaCoverTypes.Fanart) &&
                            IsTrustedImage(provider.Source, cover.RemoteUrl))
                            .GroupBy(cover => cover.CoverType).Select(group => Clone(group.First())).ToList()
                    };
                }
                catch (Exception ex)
                {
                    // Optional enrichment must neither block imports nor expose keys or response bodies.
                    _cache.BackOff(provider.Source);
                    _logger.Debug("Optional presentation lookup from {0} failed ({1}); existing metadata is retained", provider.Source, ex.GetType().Name);
                    return null;
                }
            });
        }

        private static bool NeedsPresentation(Series series, MetadataSourceType source)
        {
            return series.Overview.IsNullOrWhiteSpace() || !HasImage(series, MediaCoverTypes.Poster) ||
                (source != MetadataSourceType.MyAnimeList && !HasImage(series, MediaCoverTypes.Fanart));
        }

        private static bool SameIdentity(Series current, Series previous)
        {
            if (current.MetadataSource != previous.MetadataSource)
            {
                return false;
            }

            return current.MetadataSource == MetadataSourceType.Tvdb
                ? current.TvdbId > 0 && current.TvdbId == previous.TvdbId
                : current.ForeignId.IsNotNullOrWhiteSpace() && string.Equals(current.ForeignId, previous.ForeignId, StringComparison.Ordinal);
        }

        private static bool HasImage(Series series, MediaCoverTypes type)
        {
            return series.Images?.Any(cover => cover != null && cover.CoverType == type && cover.RemoteUrl.IsNotNullOrWhiteSpace()) == true;
        }

        private static void FillMissing(Series series, SeriesPresentation presentation)
        {
            if (presentation == null)
            {
                return;
            }

            if (series.Overview.IsNullOrWhiteSpace() && presentation.Overview.IsNotNullOrWhiteSpace())
            {
                series.Overview = presentation.Overview;
            }

            foreach (var cover in presentation.Images ?? new List<MediaCover.MediaCover>())
            {
                if (cover != null && (cover.CoverType == MediaCoverTypes.Poster || cover.CoverType == MediaCoverTypes.Fanart) &&
                    cover.RemoteUrl.IsNotNullOrWhiteSpace() && !HasImage(series, cover.CoverType))
                {
                    series.Images.RemoveAll(existing => existing?.CoverType == cover.CoverType);
                    series.Images.Add(Clone(cover));
                }
            }
        }

        private static MediaCover.MediaCover Clone(MediaCover.MediaCover cover)
        {
            return new MediaCover.MediaCover(cover.CoverType, cover.RemoteUrl);
        }

        internal static int SinglePositiveId(IEnumerable<int> ids)
        {
            var positive = ids?.Where(id => id > 0).Distinct().Take(2).ToArray();
            return positive?.Length == 1 ? positive[0] : 0;
        }

        private static Series VerifiedIdentity(Series series, Series previous)
        {
            var identity = new Series
            {
                MetadataSource = series.MetadataSource,
                MediaType = series.MediaType,
                ForeignId = series.ForeignId,
                TvdbId = Math.Max(series.TvdbId > 0 ? series.TvdbId : previous?.TvdbId ?? 0, 0),
                TmdbId = Math.Max(series.TmdbId > 0 ? series.TmdbId : previous?.TmdbId ?? 0, 0),
                MalIds = new HashSet<int>((series.MalIds?.Any(id => id > 0) == true ? series.MalIds : previous?.MalIds)?.Where(id => id > 0) ?? Enumerable.Empty<int>()),
                AniListIds = new HashSet<int>((series.AniListIds?.Any(id => id > 0) == true ? series.AniListIds : previous?.AniListIds)?.Where(id => id > 0) ?? Enumerable.Empty<int>()),
                Language = series.Language,
                SeasonType = series.SeasonType
            };

            if (int.TryParse(series.ForeignId, out var nativeId) && nativeId > 0)
            {
                if (series.MetadataSource == MetadataSourceType.AniList)
                {
                    identity.AniListIds = new HashSet<int> { nativeId };
                }
                else if (series.MetadataSource == MetadataSourceType.MyAnimeList)
                {
                    identity.MalIds = new HashSet<int> { nativeId };
                }
                else if (series.MetadataSource == MetadataSourceType.Tmdb)
                {
                    identity.TmdbId = nativeId;
                }
            }

            return identity;
        }

        private static string PresentationKey(MetadataSourceType source, Series identity)
        {
            var aniList = SinglePositiveId(identity.AniListIds);
            var mal = SinglePositiveId(identity.MalIds);
            return source switch
            {
                MetadataSourceType.Tmdb when identity.MediaType != MediaType.Movie && identity.TmdbId > 0 => "tmdb:" + identity.TmdbId,
                MetadataSourceType.AniList when aniList > 0 => "anilist:" + aniList,
                MetadataSourceType.AniList when mal > 0 => "anilist:mal:" + mal,
                MetadataSourceType.MyAnimeList when mal > 0 => "jikan:" + mal,
                MetadataSourceType.Tvdb when identity.TvdbId > 0 => $"tvdb:{identity.TvdbId}:{identity.Language?.Id}:{identity.SeasonType}",
                _ => null
            };
        }

        private static bool IsTrustedImage(MetadataSourceType source, string url)
        {
            if (url == null || url.Length > 4096 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Port != 443)
            {
                return false;
            }

            return source switch
            {
                MetadataSourceType.AniList => uri.Host == "s4.anilist.co" || uri.Host == "s3.anilist.co" || uri.Host == "s.anilist.co",
                MetadataSourceType.MyAnimeList => uri.Host == "cdn.myanimelist.net",
                MetadataSourceType.Tmdb => uri.Host == "image.tmdb.org",
                MetadataSourceType.Tvdb => uri.Host == "artworks.thetvdb.com" || uri.Host == "banners.thetvdb.com",
                _ => false
            };
        }
    }
}
