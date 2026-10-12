using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadGrabHistoryFixture : CoreTest<TrackedDownloadService>
    {
        private const string PackTitle = "Shokugeki no Soma S01-S03 + OVA (Food Wars) [1080p]";
        private List<EpisodeHistory> _history;
        private DownloadClientDefinition _client;
        private DownloadClientItem _item;

        [SetUp]
        public void Setup()
        {
            _history = new List<EpisodeHistory>();
            _client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            _item = new DownloadClientItem
            {
                Title = "Shokugeki no Soma",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1, Protocol = DownloadProtocol.Torrent }
            };
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(_item.DownloadId))
                .Returns(() => _history);
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns((ParsedEpisodeInfo parsed, int id, IEnumerable<int> ids) => new RemoteEpisode
                {
                    Series = new Series { Id = id },
                    ParsedEpisodeInfo = parsed,
                    Episodes = ids.Select(episodeId => new Episode { Id = episodeId, SeriesId = id }).ToList()
                });
        }

        private void GivenGrab()
        {
            _history.Add(new EpisodeHistory
            {
                DownloadId = _item.DownloadId,
                SeriesId = 55,
                EpisodeId = 101,
                SourceTitle = PackTitle,
                EventType = EpisodeHistoryEventType.Grabbed,
                Date = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc)
            });
        }

        [TestCase("Shokugeki no Soma - 24 [HorribleSubs][1080p]")]
        [TestCase("Unparseable imported filename")]
        public void should_use_original_grab_instead_of_newer_import_title(string importedTitle)
        {
            GivenGrab();
            _history.Add(new EpisodeHistory
            {
                SeriesId = 55,
                EpisodeId = 124,
                SourceTitle = importedTitle,
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                Date = new DateTime(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc)
            });

            var result = Subject.TrackDownload(_client, _item);

            result.RemoteEpisode.ParsedEpisodeInfo.ReleaseTitle.Should().Be(PackTitle);
            result.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1, 2, 3);
            result.RemoteEpisode.Series.Id.Should().Be(55);

            // Do not silently expand legacy grab IDs or synthesize history from imported files.
            result.RemoteEpisode.Episodes.Select(e => e.Id).Should().Equal(101);
        }

        [Test]
        public void should_not_mix_episode_ids_from_another_series()
        {
            GivenGrab();
            _history.Add(new EpisodeHistory
            {
                SeriesId = 99,
                EpisodeId = 999,
                SourceTitle = "Other Series S01",
                EventType = EpisodeHistoryEventType.Grabbed,
                Date = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc)
            });

            Subject.TrackDownload(_client, _item).RemoteEpisode.Episodes.Select(e => e.Id).Should().Equal(101);
        }

        [Test]
        public void should_retry_unresolved_blocked_import_when_history_becomes_available()
        {
            var unresolved = Subject.TrackDownload(_client, _item);
            unresolved.RemoteEpisode.Should().BeNull();
            unresolved.State = TrackedDownloadState.ImportBlocked;
            GivenGrab();

            var recovered = Subject.TrackDownload(_client, _item);

            recovered.RemoteEpisode.Should().NotBeNull();
            recovered.RemoteEpisode.ParsedEpisodeInfo.ReleaseTitle.Should().Be(PackTitle);
        }

        [Test]
        public void should_keep_cached_context_for_resolved_blocked_import()
        {
            GivenGrab();
            var tracked = Subject.TrackDownload(_client, _item);
            tracked.State = TrackedDownloadState.ImportBlocked;

            Subject.TrackDownload(_client, _item).Should().BeSameAs(tracked);
            Mocker.GetMock<IHistoryService>().Verify(s => s.FindByDownloadId(_item.DownloadId), Times.Once());
        }

        [Test]
        public void should_reload_grabbed_coverage_when_same_client_reuses_an_active_pack()
        {
            GivenGrab();
            var tracked = Subject.TrackDownload(_client, _item);
            tracked.State = TrackedDownloadState.ImportPending;
            _history.Add(new EpisodeHistory
            {
                DownloadId = _item.DownloadId,
                SeriesId = 55,
                EpisodeId = 201,
                SourceTitle = PackTitle,
                EventType = EpisodeHistoryEventType.Grabbed,
                Date = new DateTime(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc)
            });

            Subject.Handle(new EpisodeGrabbedEvent(new RemoteEpisode
            {
                Series = new Series { Id = 55 },
                Episodes = new List<Episode> { new Episode { Id = 201, SeriesId = 55 } }
            })
            {
                DownloadId = _item.DownloadId,
                DownloadClientId = _client.Id
            });

            Subject.Find(_item.DownloadId).Should().BeNull();
            var reloaded = Subject.TrackDownload(_client, _item);
            reloaded.RemoteEpisode.Episodes.Select(e => e.Id).Should().BeEquivalentTo(new[] { 101, 201 });
            reloaded.Should().NotBeSameAs(tracked);
        }

        [TestCase(2, "35238", 55)]
        [TestCase(1, "other-hash", 55)]
        [TestCase(1, "35238", 99)]
        public void unrelated_grab_should_not_invalidate_active_pack(int clientId, string downloadId, int seriesId)
        {
            GivenGrab();
            var tracked = Subject.TrackDownload(_client, _item);
            tracked.State = TrackedDownloadState.ImportPending;

            Subject.Handle(new EpisodeGrabbedEvent(new RemoteEpisode { Series = new Series { Id = seriesId } })
            {
                DownloadId = downloadId,
                DownloadClientId = clientId
            });

            Subject.Find(_item.DownloadId).Should().BeSameAs(tracked);
        }

        [Test]
        public void grab_for_another_metadata_identity_should_not_invalidate_active_pack()
        {
            GivenGrab();
            var tracked = Subject.TrackDownload(_client, _item);
            tracked.State = TrackedDownloadState.ImportPending;

            Subject.Handle(new EpisodeGrabbedEvent(new RemoteEpisode
            {
                Series = new Series { Id = 55, MetadataSource = MetadataSourceType.AniList, ForeignId = "20934" }
            })
            {
                DownloadId = _item.DownloadId,
                DownloadClientId = _client.Id
            });

            Subject.Find(_item.DownloadId).Should().BeSameAs(tracked);
        }
    }
}
