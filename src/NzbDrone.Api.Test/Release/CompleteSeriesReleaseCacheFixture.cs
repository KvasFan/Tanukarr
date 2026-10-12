using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using V3 = Sonarr.Api.V3.Indexers;
using V5 = Sonarr.Api.V5.Release;

namespace NzbDrone.Api.Test.Release
{
    [TestFixture]
    public class CompleteSeriesReleaseCacheFixture : TestBase
    {
        private const string Guid = "same-torrent";
        private const int NativeSeriesId = 101;
        private const int ForeignSeriesId = 202;
        private RemoteEpisode _complete;
        private RemoteEpisode _normal;
        private List<DownloadDecision> _completeDecisions;
        private List<DownloadDecision> _normalDecisions;

        [SetUp]
        public void Setup()
        {
            var profile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, Allowed = true }
                }
            };
            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.GetDefaultProfile(It.IsAny<string>(), It.IsAny<Quality>(), It.IsAny<Quality[]>()))
                .Returns(profile);
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                .Setup(s => s.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns<List<DownloadDecision>>(d => d);
            Mocker.GetMock<IDownloadService>()
                .Setup(s => s.DownloadReport(It.IsAny<RemoteEpisode>(), It.IsAny<int?>()))
                .Returns(Task.CompletedTask);
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.GetBySeason(It.IsAny<int>(), It.IsAny<int>(), null))
                .Returns(new List<EpisodeHistory>());

            _complete = CreateRemote(NativeSeriesId, true);
            _normal = CreateRemote(NativeSeriesId, false);
            _normal.Episodes = _normal.Episodes.Take(1).ToList();
            _completeDecisions = new List<DownloadDecision> { new DownloadDecision(_complete) };
            _normalDecisions = new List<DownloadDecision> { new DownloadDecision(_normal) };
            Mocker.GetMock<ISearchForReleases>().Setup(s => s.CompleteSeriesSearch(NativeSeriesId))
                .ReturnsAsync(() => _completeDecisions);
            Mocker.GetMock<ISearchForReleases>().Setup(s => s.CompleteSeriesSearch(ForeignSeriesId))
                .ReturnsAsync(new List<DownloadDecision> { new DownloadDecision(CreateRemote(ForeignSeriesId, true)) });
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.SeasonSearch(NativeSeriesId, 1, false, false, true, true))
                .ReturnsAsync(() => _normalDecisions);
        }

        private static RemoteEpisode CreateRemote(int seriesId, bool complete)
        {
            return new RemoteEpisode
            {
                Series = new Series
                {
                    Id = seriesId,
                    Title = "Native anime",
                    TvdbId = seriesId == NativeSeriesId ? -1234 : 999,
                    MetadataSource = seriesId == NativeSeriesId ? MetadataSourceType.AniList : MetadataSourceType.Tvdb
                },
                Episodes = new List<Episode>
                {
                    new Episode { Id = seriesId + 1, SeriesId = seriesId, SeasonNumber = 1, EpisodeNumber = 1 },
                    new Episode { Id = seriesId + 2, SeriesId = seriesId, SeasonNumber = 1, EpisodeNumber = 2 }
                },
                Release = new TorrentInfo { Guid = Guid, IndexerId = 7, Title = "Native anime COMPLETE", DownloadProtocol = DownloadProtocol.Torrent },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeriesTitle = "Native anime", FullSeason = true, IsCompleteSeries = complete,
                    SeasonNumbers = new List<int> { 1 }, Quality = new QualityModel(Quality.WEBDL1080p)
                }
            };
        }

        private async Task SearchComplete(int version, int seriesId = NativeSeriesId)
        {
            if (version == 3)
            {
                var resources = await Mocker.Resolve<V3.ReleaseController>().GetReleases(seriesId, null, null, true);
                resources.Should().OnlyContain(r => r.CompleteSeries && r.SeriesId == seriesId);
            }
            else
            {
                await Mocker.Resolve<V5.ReleaseController>().GetReleases(seriesId, null, null, true);
            }
        }

        private async Task SearchSeason(int version)
        {
            if (version == 3)
            {
                await Mocker.Resolve<V3.ReleaseController>().GetReleases(NativeSeriesId, null, 1);
            }
            else
            {
                await Mocker.Resolve<V5.ReleaseController>().GetReleases(NativeSeriesId, null, 1);
            }
        }

        private async Task Grab(int version, bool complete, int? seriesId = NativeSeriesId)
        {
            if (version == 3)
            {
                await Mocker.Resolve<V3.ReleaseController>().DownloadRelease(new V3.ReleaseResource
                {
                    IndexerId = 7, Guid = Guid, CompleteSeries = complete, SeriesId = seriesId
                });
            }
            else
            {
                await Mocker.Resolve<V5.ReleaseController>().DownloadRelease(new V5.ReleaseGrabResource
                {
                    IndexerId = 7, Guid = Guid,
                    SearchInfo = new V5.SearchInfoResource { CompleteSeries = complete, SeriesId = seriesId }
                });
            }
        }

        [TestCase(3, false)]
        [TestCase(3, true)]
        [TestCase(5, false)]
        [TestCase(5, true)]
        public async Task complete_search_should_keep_native_series_and_episode_coverage_after_same_guid_season_search(int version, bool foreignSeries)
        {
            if (foreignSeries)
            {
                _normalDecisions = new List<DownloadDecision> { new DownloadDecision(CreateRemote(ForeignSeriesId, false)) };
            }

            await SearchComplete(version);
            await SearchSeason(version);
            await Grab(version, true);

            Mocker.GetMock<IDownloadService>().Verify(
                s => s.DownloadReport(
                    It.Is<RemoteEpisode>(r => r == _complete && r.Series.Id == NativeSeriesId && r.Episodes.Count == 2),
                    null),
                Times.Once());
        }

        [TestCase(3)]
        [TestCase(5)]
        public async Task complete_search_should_be_scoped_to_series_even_when_another_complete_search_returns_same_guid(int version)
        {
            await SearchComplete(version);
            await SearchComplete(version, ForeignSeriesId);
            await Grab(version, true);

            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(_complete, null), Times.Once());
        }

        [TestCase(3)]
        [TestCase(5)]
        public async Task complete_search_should_leave_existing_normal_grab_cache_compatible(int version)
        {
            await SearchSeason(version);
            await SearchComplete(version);
            await Grab(version, false);

            Mocker.GetMock<IDownloadService>().Verify(s => s.DownloadReport(_normal, null), Times.Once());
        }

        [TestCase(3, false)]
        [TestCase(3, true)]
        [TestCase(5, false)]
        [TestCase(5, true)]
        public async Task complete_grab_should_reject_unverified_or_wrong_series_cached_release(int version, bool wrongSeries)
        {
            _completeDecisions = new List<DownloadDecision>
            {
                new DownloadDecision(CreateRemote(wrongSeries ? ForeignSeriesId : NativeSeriesId, wrongSeries))
            };
            await SearchComplete(version);

            Assert.ThrowsAsync<NzbDroneClientException>(async () => await Grab(version, true));
            Mocker.GetMock<IDownloadService>().VerifyNoOtherCalls();
        }

        [TestCase(3)]
        [TestCase(5)]
        public void complete_grab_should_require_series_scope(int version)
        {
            Assert.ThrowsAsync<NzbDroneClientException>(async () => await Grab(version, true, null));

            Mocker.GetMock<IDownloadService>().VerifyNoOtherCalls();
        }
    }
}
