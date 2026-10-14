using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Tv
{
    public interface IAddSeriesService
    {
        Series AddSeries(Series newSeries);
        List<Series> AddSeries(List<Series> newSeries, bool ignoreErrors = false);
    }

    public class AddSeriesService : IAddSeriesService
    {
        private readonly ISeriesService _seriesService;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly IAllocateSyntheticSeriesIds _syntheticSeriesIds;
        private readonly IAnimeGroupingService _animeGroupingService;
        private readonly IAnimeGroupPathBuilder _animeGroupPathBuilder;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IAddSeriesValidator _addSeriesValidator;
        private readonly Logger _logger;

        public AddSeriesService(ISeriesService seriesService,
                                IMetadataProviderFactory metadataProviderFactory,
                                IAllocateSyntheticSeriesIds syntheticSeriesIds,
                                IAnimeGroupingService animeGroupingService,
                                IAnimeGroupPathBuilder animeGroupPathBuilder,
                                IBuildFileNames fileNameBuilder,
                                IAddSeriesValidator addSeriesValidator,
                                Logger logger)
        {
            _seriesService = seriesService;
            _metadataProviderFactory = metadataProviderFactory;
            _syntheticSeriesIds = syntheticSeriesIds;
            _animeGroupingService = animeGroupingService;
            _animeGroupPathBuilder = animeGroupPathBuilder;
            _fileNameBuilder = fileNameBuilder;
            _addSeriesValidator = addSeriesValidator;
            _logger = logger;
        }

        public Series AddSeries(Series newSeries)
        {
            Ensure.That(newSeries, () => newSeries).IsNotNull();

            newSeries = AddSkyhookData(newSeries);
            newSeries = SetPropertiesAndValidate(newSeries);

            LogAddingSeries(newSeries);
            _seriesService.AddSeries(newSeries);

            return newSeries;
        }

        public List<Series> AddSeries(List<Series> newSeries, bool ignoreErrors = false)
        {
            var added = DateTime.UtcNow;
            var seriesToAdd = new List<Series>();
            var existingSeriesTvdbIds = _seriesService.AllSeriesTvdbIds();
            var throttledSources = new HashSet<MetadataSourceType>();

            foreach (var s in newSeries)
            {
                if (ignoreErrors && throttledSources.Contains(s.MetadataSource))
                {
                    continue;
                }

                try
                {
                    var series = AddSkyhookData(s);
                    series = SetPropertiesAndValidate(series);
                    AnimeGroupingValidator.ValidateSlot(series, seriesToAdd);
                    series.Added = added;
                    if (existingSeriesTvdbIds.ContainsValue(series.TvdbId))
                    {
                        _logger.Debug("TVDB ID {0} was not added due to validation failure: Series {1} already exists in database", s.TvdbId, s);
                        continue;
                    }

                    if (seriesToAdd.Any(f => f.TvdbId == series.TvdbId))
                    {
                        _logger.Trace("TVDB ID {0} was already added from another import list, not adding series {1} again", s.TvdbId, s);
                        continue;
                    }

                    var duplicateSlug = seriesToAdd.FirstOrDefault(f => f.TitleSlug == series.TitleSlug);
                    if (duplicateSlug != null)
                    {
                        _logger.Debug("TVDB ID {0} was not added due to validation failure: Duplicate Slug {1} used by series {2}", s.TvdbId, s.TitleSlug, duplicateSlug.TvdbId);
                        continue;
                    }

                    LogAddingSeries(series);
                    seriesToAdd.Add(series);
                }
                catch (TooManyRequestsException) when (ignoreErrors)
                {
                    // Keep the metadata already prepared and stop spending this provider's
                    // shared allowance. The complete import-list snapshot remains available
                    // for a later sync; other metadata providers can still make progress.
                    throttledSources.Add(s.MetadataSource);
                    _logger.Warn("Metadata source {0} reached its request limit. Prepared series will be saved; pending entries will be retried by a later list sync.", s.MetadataSource);
                }
                catch (HttpException) when (ignoreErrors)
                {
                    // A transient provider failure must not discard earlier successful
                    // entries or expose provider response bodies in an import-list log.
                    _logger.Warn("Metadata could not be retrieved from {0}. This entry remains pending for a later list sync.", s.MetadataSource);
                }
                catch (ValidationException ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

                    _logger.Debug("Series {0} with TVDB ID {1} was not added due to validation failures. {2}", s, s.TvdbId, ex.Message);
                }
            }

            return _seriesService.AddSeries(seriesToAdd, ignoreErrors);
        }

        private Series AddSkyhookData(Series newSeries)
        {
            // Automatic facts are resolved by the server, including import-list additions.
            // A lookup/preview response must not let a new client manufacture membership.
            if (newSeries.AnimeGroupingMode == AnimeGroupingMode.Automatic)
            {
                ClearAutomaticGroup(newSeries);
            }

            Tuple<Series, List<Episode>> tuple;

            try
            {
                // Fetched from whichever provider the request names, so a series can be added
                // from TMDB or AniList. Requests that name none default to Tvdb, which is the
                // same SkyHook lookup as before.
                tuple = _metadataProviderFactory.GetProvider(newSeries).GetSeriesInfo(newSeries);
            }
            catch (SeriesNotFoundException)
            {
                _logger.Error("Series [{0}:{1}][{2}] was not found in the selected metadata provider. Path: {3}", newSeries.MetadataSource, NativeIdForLog(newSeries), newSeries.Title, newSeries.Path);

                var field = newSeries.MetadataSource == MetadataSourceType.Tvdb ? "TvdbId" : "ForeignId";
                var identity = newSeries.MetadataSource == MetadataSourceType.Tvdb ? (object)newSeries.TvdbId : newSeries.ForeignId;

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure(field, $"A series with this ID was not found in the selected metadata provider. Path: {newSeries.Path}", identity)
                                              });
            }

            var series = tuple.Item1;

            if (newSeries.AnimeGroupingMode == AnimeGroupingMode.Automatic)
            {
                ClearAutomaticGroup(series, preserveProviderMemberKind: true);
            }

            // If seasons were passed in on the new series use them, otherwise use the seasons from Skyhook
            newSeries.Seasons = newSeries.Seasons != null && newSeries.Seasons.Any() ? newSeries.Seasons : series.Seasons;

            series.ApplyChanges(newSeries);

            // Set outside ApplyChanges, which is shared with the edit path and must not take
            // these from a request. The provider populates its own source and id; the chosen
            // ordering is the user's and is fixed for the life of the series, because season
            // and episode numbers appear in file and folder names.
            series.OrderingId = newSeries.OrderingId;
            _animeGroupingService.Apply(series);

            // A series the provider could not map to TheTVDB still needs a distinct value
            // here, since the column is uniquely indexed and lookups expect a single match.
            if (series.TvdbId == 0)
            {
                series.TvdbId = _syntheticSeriesIds.AllocateTvdbId();
            }

            return series;
        }

        private Series SetPropertiesAndValidate(Series newSeries)
        {
            AnimeGroupingValidator.Validate(newSeries, _seriesService.GetAllSeries());

            if (string.IsNullOrWhiteSpace(newSeries.Path))
            {
                if (AnimeGroupingValidator.HasGroup(newSeries))
                {
                    newSeries.Path = _animeGroupPathBuilder.BuildPath(newSeries);
                    newSeries.SeasonFolder = false;
                }
                else
                {
                    var folderName = _fileNameBuilder.GetSeriesFolder(newSeries);
                    newSeries.Path = Path.Combine(newSeries.RootFolderPath, folderName);
                }
            }

            newSeries.CleanTitle = newSeries.Title.CleanSeriesTitle();
            newSeries.SortTitle = SeriesTitleNormalizer.Normalize(newSeries.Title, newSeries.TvdbId);
            newSeries.Added = DateTime.UtcNow;

            if (newSeries.AddOptions != null && newSeries.AddOptions.Monitor == MonitorTypes.None)
            {
                newSeries.Monitored = false;
            }

            var validationResult = _addSeriesValidator.Validate(newSeries);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newSeries;
        }

        private void LogAddingSeries(Series series)
        {
            var tvdbId = series.TvdbId > 0 ? series.TvdbId.ToString(CultureInfo.InvariantCulture) : "unmapped";
            var placeholder = series.TvdbId < 0 ? series.TvdbId.ToString(CultureInfo.InvariantCulture) : "none";
            _logger.Info("Adding Series [{0}:{1}][{2}] TheTVDB ID: {3}; internal placeholder: {4}; Path: [{5}]",
                series.MetadataSource,
                NativeIdForLog(series),
                series.Title,
                tvdbId,
                placeholder,
                series.Path);
        }

        private static string NativeIdForLog(Series series)
        {
            return series.ForeignId.IsNotNullOrWhiteSpace()
                ? series.ForeignId
                : series.TvdbId > 0 ? series.TvdbId.ToString(CultureInfo.InvariantCulture) : "unknown";
        }

        private static void ClearAutomaticGroup(Series series, bool preserveProviderMemberKind = false)
        {
            series.AnimeGroupId = null;
            series.AnimeGroupTitle = null;
            series.AnimeGroupSeasonNumber = null;
            series.AnimeGroupMovieNumber = null;
            series.AnimeGroupFolder = null;
            series.AnimeGroupMemberFolder = null;
            if (!preserveProviderMemberKind)
            {
                series.AnimeGroupMemberKind = AnimeGroupMemberKind.Season;
            }

            series.AnimeGroupKind = AnimeGroupKind.Series;
            series.AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Automatic;
        }
    }
}
