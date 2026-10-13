using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Grouping
{
    public interface IAnimeGroupingService
    {
        AnimeGroupingResult Resolve(Series series);
        void Apply(Series series);
    }

    public class AnimeGroupingService : IAnimeGroupingService
    {
        public const int MaxGroupIdLength = 96;
        public const int MaxGroupTitleLength = 180;
        public const int MaxSeasonNumber = 999;
        public const int MaxGroupFolderBytes = 180;

        private static readonly Regex GroupIdRegex = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9._:-]*\z", RegexOptions.Compiled);
        private static readonly Regex ReservedFolderRegex = new Regex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IAnimeRelationProvider[] _providers;
        private readonly ISeriesService _seriesService;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly Logger _logger;

        public AnimeGroupingService(IEnumerable<IAnimeRelationProvider> providers, ISeriesService seriesService, IBuildFileNames fileNameBuilder, Logger logger)
        {
            _providers = providers.ToArray();
            _seriesService = seriesService;
            _fileNameBuilder = fileNameBuilder;
            _logger = logger;
        }

        public static bool IsValidGroupId(string id)
        {
            return id.IsNotNullOrWhiteSpace() && id.Length <= MaxGroupIdLength && GroupIdRegex.IsMatch(id);
        }

        public static bool IsValidGroupTitle(string title)
        {
            return title.IsNotNullOrWhiteSpace() && title.Length <= MaxGroupTitleLength && !title.Any(char.IsControl);
        }

        public static string CreateGroupFolder(string title)
        {
            var folder = FileNameBuilder.CleanFolderName(FileNameBuilder.CleanFileName(title ?? string.Empty));
            folder = new string(folder.Where(c => !char.IsControl(c)).ToArray()).Trim(' ', '.');

            if (folder.IsNullOrWhiteSpace())
            {
                folder = "Anime";
            }

            folder = ReservedFolderRegex.IsMatch(folder) ? "_" + folder : folder;
            return folder.Truncate(MaxGroupFolderBytes).TrimEnd(' ', '.');
        }

        public AnimeGroupingResult Resolve(Series series)
        {
            if (series.AnimeGroupingMode == AnimeGroupingMode.Disabled)
            {
                return Result(series, AnimeGroupingStatus.Disabled);
            }

            if (series.AnimeGroupingMode == AnimeGroupingMode.Manual)
            {
                if (!HasValidGroup(series))
                {
                    return Result(series, AnimeGroupingStatus.Ambiguous);
                }

                var manual = FromSeries(series, AnimeGroupingStatus.Manual);
                return WithFolders(series, manual, _seriesService.GetAllSeries() ?? new List<Series>());
            }

            // Existing membership is fixed. A later provider correction must not silently
            // reassign a season or move files already belonging to a stored group.
            if (series.Id > 0 && HasValidGroup(series) && series.AnimeGroupFolder.IsNotNullOrWhiteSpace())
            {
                return WithFolders(series, FromSeries(series, AnimeGroupingStatus.Existing), _seriesService.GetAllSeries() ?? new List<Series>());
            }

            var provider = _providers.FirstOrDefault(p => p.Source == series.MetadataSource);

            if (provider == null ||
                (series.MetadataSource != MetadataSourceType.AniList && series.MetadataSource != MetadataSourceType.MyAnimeList) ||
                !int.TryParse(series.ForeignId, out var id) || id <= 0)
            {
                return Result(series, AnimeGroupingStatus.Unsupported);
            }

            try
            {
                var existing = _seriesService.GetAllSeries() ?? new List<Series>();
                var result = AnimeGroupingResolver.Resolve(series.MetadataSource, series.ForeignId, provider.GetAnimeRelations, existing);
                return result.IsResolved ? WithFolders(series, result, existing) : result;
            }
            catch (Exception ex)
            {
                // Grouping is optional. Normal metadata/import behavior remains available
                // when relationship lookup is unavailable or incomplete.
                _logger.Debug(ex, "Unable to resolve anime grouping for {0}:{1}", series.MetadataSource, series.ForeignId);
                return Result(series, AnimeGroupingStatus.Unavailable);
            }
        }

        public void Apply(Series series)
        {
            var result = Resolve(series);

            if (result.Status == AnimeGroupingStatus.Disabled)
            {
                series.AnimeGroupId = null;
                series.AnimeGroupTitle = null;
                series.AnimeGroupSeasonNumber = null;
                series.AnimeGroupFolder = null;
                series.AnimeGroupMovieNumber = null;
                series.AnimeGroupMemberFolder = null;
            }
            else if (result.IsResolved)
            {
                series.AnimeGroupId = result.AnimeGroupId;
                series.AnimeGroupTitle = result.AnimeGroupTitle;
                series.AnimeGroupSeasonNumber = result.AnimeGroupSeasonNumber;
                series.AnimeGroupFolder = result.AnimeGroupFolder;
                series.AnimeGroupMemberKind = result.AnimeGroupMemberKind;
                series.AnimeGroupKind = result.AnimeGroupKind;
                series.AnimeGroupMovieFolderStyle = result.AnimeGroupMovieFolderStyle;
                series.AnimeGroupMovieNumber = result.AnimeGroupMovieNumber;
                series.AnimeGroupMemberFolder = result.AnimeGroupMemberFolder;
            }
        }

        internal static bool HasValidGroup(Series series)
        {
            return IsValidGroupId(series.AnimeGroupId) && IsValidGroupTitle(series.AnimeGroupTitle) &&
                   Enum.IsDefined(series.AnimeGroupMemberKind) && Enum.IsDefined(series.AnimeGroupKind) &&
                   Enum.IsDefined(series.AnimeGroupMovieFolderStyle) &&
                   (series.AnimeGroupMemberKind != AnimeGroupMemberKind.Season || series.AnimeGroupKind != AnimeGroupKind.MovieSaga) &&
                   (series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie
                       ? series.AnimeGroupMovieNumber is >= 1 and <= MaxSeasonNumber && series.AnimeGroupSeasonNumber == null
                       : series.AnimeGroupSeasonNumber is >= 1 and <= MaxSeasonNumber && series.AnimeGroupMovieNumber == null);
        }

        internal static AnimeGroupingResult FromSeries(Series series, AnimeGroupingStatus status)
        {
            return new AnimeGroupingResult
            {
                AnimeGroupingMode = series.AnimeGroupingMode,
                Status = status,
                AnimeGroupId = series.AnimeGroupId,
                AnimeGroupTitle = series.AnimeGroupTitle,
                AnimeGroupSeasonNumber = series.AnimeGroupSeasonNumber,
                AnimeGroupFolder = series.AnimeGroupFolder,
                AnimeGroupMemberKind = series.AnimeGroupMemberKind,
                AnimeGroupKind = series.AnimeGroupKind,
                AnimeGroupMovieFolderStyle = series.AnimeGroupMovieFolderStyle,
                AnimeGroupMovieNumber = series.AnimeGroupMovieNumber,
                AnimeGroupMemberFolder = series.AnimeGroupMemberFolder
            };
        }

        private AnimeGroupingResult WithFolders(Series series, AnimeGroupingResult result, List<Series> existingSeries)
        {
            var groupMembers = existingSeries.Where(s => s.AnimeGroupId == result.AnimeGroupId &&
                s.AnimeGroupingMode != AnimeGroupingMode.Disabled && HasValidGroup(s)).ToList();
            var canonical = groupMembers.OrderByDescending(s => s.AnimeGroupMemberKind == AnimeGroupMemberKind.Season || s.AnimeGroupKind == AnimeGroupKind.Series)
                .FirstOrDefault(s => s.Id != series.Id && s.AnimeGroupFolder.IsNotNullOrWhiteSpace());

            if (canonical != null)
            {
                result.AnimeGroupTitle = canonical.AnimeGroupTitle;
                result.AnimeGroupFolder = canonical.AnimeGroupFolder;
                result.AnimeGroupKind = result.AnimeGroupMemberKind == AnimeGroupMemberKind.Season ||
                                       result.AnimeGroupKind == AnimeGroupKind.Series ||
                                       groupMembers.Any(s => s.AnimeGroupMemberKind == AnimeGroupMemberKind.Season || s.AnimeGroupKind == AnimeGroupKind.Series)
                    ? AnimeGroupKind.Series
                    : canonical.AnimeGroupKind;
            }

            var namingSeries = new Series
            {
                Id = series.Id,
                Title = series.Title.IsNotNullOrWhiteSpace() ? series.Title : result.MemberTitle,
                Year = series.Year,
                MetadataSource = series.MetadataSource,
                ForeignId = series.ForeignId,
                TvdbId = series.TvdbId,
                TmdbId = series.TmdbId,
                ImdbId = series.ImdbId,
                AnimeGroupId = result.AnimeGroupId,
                AnimeGroupTitle = result.AnimeGroupTitle,
                AnimeGroupKind = result.AnimeGroupKind,
                AnimeGroupMemberKind = result.AnimeGroupMemberKind,
                AnimeGroupSeasonNumber = result.AnimeGroupSeasonNumber,
                AnimeGroupMovieNumber = result.AnimeGroupMovieNumber,
                AnimeGroupMovieFolderStyle = result.AnimeGroupMovieFolderStyle
            };

            result.AnimeGroupFolder = canonical?.AnimeGroupFolder ??
                (series.Id > 0 && series.AnimeGroupFolder.IsNotNullOrWhiteSpace()
                    ? series.AnimeGroupFolder
                    : _fileNameBuilder.GetAnimeGroupFolder(namingSeries));

            if (series.Id > 0 && series.AnimeGroupMemberFolder.IsNotNullOrWhiteSpace())
            {
                result.AnimeGroupMemberFolder = series.AnimeGroupMemberFolder;
            }
            else if (series.Id > 0 && series.Path.IsNotNullOrWhiteSpace() &&
                     string.Equals(Path.GetFileName(Path.GetDirectoryName(series.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))), result.AnimeGroupFolder, StringComparison.OrdinalIgnoreCase))
            {
                result.AnimeGroupMemberFolder = Path.GetFileName(series.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            else
            {
                result.AnimeGroupMemberFolder = _fileNameBuilder.GetAnimeGroupMemberFolder(namingSeries);
            }

            return result;
        }

        private static AnimeGroupingResult Result(Series series, AnimeGroupingStatus status)
        {
            return new AnimeGroupingResult
            {
                AnimeGroupingMode = series.AnimeGroupingMode,
                Status = status,
                AnimeGroupMemberKind = series.AnimeGroupMemberKind,
                AnimeGroupKind = series.AnimeGroupKind,
                AnimeGroupMovieFolderStyle = series.AnimeGroupMovieFolderStyle
            };
        }
    }
}
