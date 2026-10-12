using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.LibraryTools;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ManualNumberingOverrideFixture : CoreTest<ImportDecisionMaker>
    {
        [Test]
        public void exact_manual_selection_should_survive_real_numbering_and_folder_specs()
        {
            var series = new Series
            {
                Id = 42,
                TvdbId = 73141,
                Title = "Example Show",
                Path = @"C:\library\Example Show".AsOsAgnostic(),
                UseSceneNumbering = true,
                SeasonType = SeasonType.Official
            };
            var episodes = new List<Episode>
            {
                new Episode { Id = 51, SeriesId = 42, SeasonNumber = 5, EpisodeNumber = 1, SceneSeasonNumber = 4, SceneEpisodeNumber = 1, Title = "1600 Candles" },
                new Episode { Id = 61, SeriesId = 42, SeasonNumber = 6, EpisodeNumber = 1, SceneSeasonNumber = 5, SceneEpisodeNumber = 1, Title = "In Country... Club" }
            };
            var pack = Parser.Parser.ParseTitle("Example.Show.S05.1080p-WEBGroup");
            var history = new List<EpisodeHistory>
            {
                new EpisodeHistory
                {
                    SeriesId = 42,
                    EpisodeId = 61,
                    SourceTitle = pack.ReleaseTitle,
                    EventType = EpisodeHistoryEventType.Grabbed,
                    Data = new Dictionary<string, string>
                    {
                        { "ManualEpisodeMapping", "true" },
                        { "ManualEpisodeIds", "[61]" }
                    }
                }
            };
            var download = new DownloadClientItem { DownloadId = "manual-pack", Title = pack.ReleaseTitle };
            var files = new List<string> { @"C:\downloads\Example.Show.S05E01.1600.Candles.1080p.mkv".AsOsAgnostic() };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.FilterExistingFiles(files, series)).Returns(files);
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId(download.DownloadId)).Returns(history);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(42)).Returns(episodes);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => episodes.Where(e => ids.Contains(e.Id)).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeason(42, It.IsAny<int>()))
                .Returns<int, int>((id, season) => episodes.Where(e => e.SeasonNumber == season).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySceneSeason(42, It.IsAny<int>()))
                .Returns<int, int>((id, season) => episodes.Where(e => e.SceneSeasonNumber == season).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisode(42, It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int, int>((id, season, number) => episodes.SingleOrDefault(e => e.SeasonNumber == season && e.EpisodeNumber == number));
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisodesBySceneNumbering(42, It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int, int>((id, season, number) => episodes.Where(e => e.SceneSeasonNumber == season && e.SceneEpisodeNumber == number).ToList());
            Mocker.GetMock<ISceneMappingService>().Setup(s => s.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping { SceneOrigin = "scene" });
            Mocker.SetConstant<IEpisodeNumberingResolver>(Mocker.Resolve<EpisodeNumberingResolver>());
            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());
            Mocker.SetConstant<IManualEpisodeMappingService>(Mocker.Resolve<ManualEpisodeMappingService>());
            var folderSpec = Mocker.Resolve<MatchesFolderSpecification>();
            var grabSpec = Mocker.Resolve<MatchesGrabSpecification>();
            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification>>(new IImportDecisionEngineSpecification[] { folderSpec, grabSpec });
            var aggregator = Mocker.Resolve<AggregateEpisodes>();
            Mocker.GetMock<IAggregationService>()
                .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), download))
                .Callback<LocalEpisode, DownloadClientItem>((local, item) =>
                {
                    aggregator.Aggregate(local, item);
                    local.Release = new GrabbedReleaseInfo(history);
                });

            var decision = Subject.GetImportDecisions(files, series, download, pack, pack, true).Single();

            decision.Approved.Should().BeTrue();
            decision.LocalEpisode.ManualEpisodeMapping.Should().BeTrue();
            decision.LocalEpisode.Episodes.Should().Equal(episodes[1]);
            var unexpected = new LocalEpisode
            {
                ManualEpisodeMapping = true,
                Episodes = new List<Episode> { episodes[0] },
                Release = new GrabbedReleaseInfo(history)
            };
            grabSpec.IsSatisfiedBy(unexpected, download).Accepted.Should().BeFalse();
        }
    }
}
