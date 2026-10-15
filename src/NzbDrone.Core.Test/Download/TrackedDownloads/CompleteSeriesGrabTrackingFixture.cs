using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class CompleteSeriesGrabTrackingFixture : CoreTest<TrackedDownloadService>
    {
        private const string CompleteTitle = "Dharma and Greg COMPLETE HULU WEB-DL AAC2 0 H 264-BTW";
        private Series _series;
        private List<Episode> _episodes;
        private List<EpisodeHistory> _history;
        private DownloadClientDefinition _client;
        private DownloadClientItem _item;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 93, TvdbId = 72406, Title = "Dharma & Greg", Year = 1997 };
            _episodes = Enumerable.Range(1, 5).SelectMany(season => Enumerable.Range(1, season == 1 ? 23 : 24)
                .Select(number => new Episode { Id = (season * 100) + number, SeriesId = 93, SeasonNumber = season, EpisodeNumber = number })).ToList();
            _history = _episodes.Select(e => new EpisodeHistory
            {
                SeriesId = 93,
                EpisodeId = e.Id,
                SourceTitle = CompleteTitle,
                EventType = EpisodeHistoryEventType.Grabbed,
                Date = new DateTime(2026, 9, 30, 17, 34, 35, DateTimeKind.Utc)
            }).ToList();
            _client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            _item = new DownloadClientItem
            {
                Title = "Dharma.and.Greg.COMPLETE.HULU.WEB-DL.AAC2.0.H.264-BTW",
                DownloadId = "054F0EC132187D77E7780DFCB1D26DE109260457",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1, Protocol = DownloadProtocol.Torrent }
            };

            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId(_item.DownloadId)).Returns(() => _history);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(93)).Returns(() => _series);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>())).Returns(() => _episodes);
            Mocker.GetMock<ISceneMappingService>().Setup(s => s.GetSceneNames(It.IsAny<int>(), It.IsAny<List<int>>(), It.IsAny<List<int>>()))
                .Returns(new List<string>());
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>()))
                .Returns((ParsedEpisodeInfo parsed, int seriesId, IEnumerable<int> ids) => new RemoteEpisode
                {
                    Series = _series,
                    ParsedEpisodeInfo = parsed,
                    Episodes = _episodes.Where(e => ids.Contains(e.Id)).ToList()
                });
        }

        [Test]
        public void should_recover_all_119_grabbed_episodes_for_real_complete_title()
        {
            var remote = Subject.TrackDownload(_client, _item).RemoteEpisode;

            remote.Series.Id.Should().Be(93);
            remote.Episodes.Select(e => e.Id).Should().BeEquivalentTo(_episodes.Select(e => e.Id));
            remote.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1, 2, 3, 4, 5);
            remote.ParsedEpisodeInfo.FullSeason.Should().BeTrue();
            remote.ParsedEpisodeInfo.IsMultiSeason.Should().BeTrue();
            remote.ParsedEpisodeInfo.ReleaseTitle.Should().Be(CompleteTitle);
            remote.ParsedEpisodeInfo.ReleaseGroup.Should().Be("BTW");
            remote.ParsedEpisodeInfo.Quality.Quality.Name.Should().Be("WEBDL-480p");
        }

        [Test]
        public void should_use_grab_instead_of_later_imported_filename()
        {
            _history.Add(new EpisodeHistory
            {
                SeriesId = 93, EpisodeId = 301, SourceTitle = "Dharma.and.Greg.S03E01.mkv",
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                Date = new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc)
            });

            Subject.TrackDownload(_client, _item).RemoteEpisode.Episodes.Should().HaveCount(119);
        }

        [Test]
        public void should_not_expand_historical_coverage_to_other_library_episodes()
        {
            _history = _history.Where(h => h.EpisodeId == 301 || h.EpisodeId == 302).ToList();
            _episodes = _episodes.Where(e => e.Id == 301 || e.Id == 302).ToList();

            var remote = Subject.TrackDownload(_client, _item).RemoteEpisode;

            remote.Episodes.Select(e => e.Id).Should().Equal(301, 302);
            remote.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(3);
        }

        [TestCase("Unknown client folder")]
        [TestCase("Dharma.and.Greg.COMPLETE.HULU.WEB-DL.AAC2.0.H.264-BTW")]
        public void should_recover_from_original_grab_even_when_client_title_changed(string clientTitle)
        {
            _item.Title = clientTitle;
            Subject.TrackDownload(_client, _item).RemoteEpisode.Episodes.Should().HaveCount(119);
        }

        [Test]
        public void should_keep_complete_coverage_when_client_renames_pack_to_parseable_episode()
        {
            _item.Title = "Dharma.and.Greg.S01E01.1080p.WEB-DL";
            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), 0, 0, null, null))
                .Returns(new RemoteEpisode
                {
                    Series = _series,
                    Episodes = _episodes.Take(1).ToList(),
                    ParsedEpisodeInfo = Parser.Parser.ParseTitle(_item.Title)
                });

            var remote = Subject.TrackDownload(_client, _item).RemoteEpisode;

            remote.Episodes.Should().HaveCount(119);
            remote.ParsedEpisodeInfo.ReleaseTitle.Should().Be(CompleteTitle);
        }

        [TestCase("Dharma and Greg (1997) COMPLETE WEB-DL")]
        [TestCase("Dharma and Greg Entire Series WEB-DL")]
        [TestCase("Dharma and Greg Box Set WEB-DL")]
        public void should_support_accepted_complete_conventions(string title)
        {
            _history.ForEach(h => h.SourceTitle = title);
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().NotBeNull();
        }

        [TestCase("Other Show COMPLETE WEB-DL")]
        [TestCase("Dharma and Greg (2019) COMPLETE WEB-DL")]
        [TestCase("Dharma and Greg Adventures COMPLETE WEB-DL")]
        [TestCase("Dharma and Greg COMPLETE Season One WEB-DL")]
        [TestCase("Dharma and Greg WEB-DL")]
        public void should_reject_unrelated_ambiguous_or_wrong_year_history(string title)
        {
            _history.ForEach(h => h.SourceTitle = title);
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_support_known_anime_alias()
        {
            _series.Title = "Food Wars!";
            _series.SeriesType = SeriesTypes.Anime;
            _series.AlternateTitles = new List<string> { "Shokugeki no Soma" };
            _history.ForEach(h => h.SourceTitle = "Shokugeki no Soma COMPLETE 1080p");
            Subject.TrackDownload(_client, _item).RemoteEpisode.ParsedEpisodeInfo.SeriesTitle.Should().Be("Shokugeki no Soma");
        }

        [Test]
        public void should_support_known_scene_alias()
        {
            Mocker.GetMock<ISceneMappingService>().Setup(s => s.GetSceneNames(72406, It.IsAny<List<int>>(), It.IsAny<List<int>>()))
                .Returns(new List<string> { "Known Alternate Name" });
            _history.ForEach(h => h.SourceTitle = "Known Alternate Name COMPLETE WEB-DL");
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().NotBeNull();
        }

        [Test]
        public void should_reject_mixed_series_grab_history()
        {
            _history[0].SeriesId = 99;
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_reject_conflicting_grab_titles()
        {
            _history[0].SourceTitle = "Other Show COMPLETE";
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_not_recover_without_grab_history()
        {
            _history.Clear();
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_not_use_only_import_history_as_grab_context()
        {
            _history.ForEach(h => h.EventType = EpisodeHistoryEventType.DownloadFolderImported);
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_reject_missing_grabbed_episode()
        {
            _episodes.RemoveAt(0);
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_reject_episode_from_another_series()
        {
            _episodes[0].SeriesId = 99;
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_reject_specials_in_regular_complete_context()
        {
            _episodes[0].SeasonNumber = 0;
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_reject_deleted_series()
        {
            _series = null;
            Subject.TrackDownload(_client, _item).RemoteEpisode.Should().BeNull();
        }

        [Test]
        public void should_retry_unresolved_blocked_complete_import()
        {
            var grabs = _history;
            _history = new List<EpisodeHistory>();
            var unresolved = Subject.TrackDownload(_client, _item);
            unresolved.State = TrackedDownloadState.ImportBlocked;
            _history = grabs;

            Subject.TrackDownload(_client, _item).RemoteEpisode.Episodes.Should().HaveCount(119);
        }

        [Test]
        public void should_preserve_complete_grab_context_when_series_is_edited()
        {
            var tracked = Subject.TrackDownload(_client, _item);
            tracked.State = TrackedDownloadState.ImportPending;

            Subject.Handle(new SeriesEditedEvent(_series, _series));

            tracked.RemoteEpisode.Episodes.Should().HaveCount(119);
            tracked.RemoteEpisode.ParsedEpisodeInfo.ReleaseTitle.Should().Be(CompleteTitle);
            tracked.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void should_recover_native_anilist_identity_without_tvdb_scene_lookups()
        {
            _series.MetadataSource = MetadataSourceType.AniList;
            _series.ForeignId = "20934";
            _series.TvdbId = -20934;
            _series.AniListIds.Add(20934);

            var remote = Subject.TrackDownload(_client, _item).RemoteEpisode;

            remote.Series.MetadataSource.Should().Be(MetadataSourceType.AniList);
            remote.Series.ForeignId.Should().Be("20934");
            remote.Episodes.Select(e => e.Id).Should().Equal(_episodes.Select(e => e.Id));
            Mocker.GetMock<ISceneMappingService>()
                .Verify(s => s.GetSceneNames(It.IsAny<int>(), It.IsAny<List<int>>(), It.IsAny<List<int>>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_verified_movie_context_before_legacy_numeric_title_parsing_and_after_edits(bool edit)
        {
            const string title = "Generated Feature Collection.2019-2021.720p.WEB-DL.H264-Fixture";
            _series.MediaType = MediaType.Movie;
            _series.MetadataSource = MetadataSourceType.TmdbMovie;
            _series.ForeignId = "970001";
            _series.TvdbId = -1;
            _episodes = _episodes.Take(1).ToList();
            _history = _history.Take(1).ToList();
            _history[0].SourceTitle = title;
            _item.Title = title;
            var parsed = new ParsedEpisodeInfo
            {
                IsMovie = true, IsMovieCollectionPack = true, MovieCollectionId = "manual:feature-collection",
                ReleaseTitle = title, SeasonNumber = 1, EpisodeNumbers = new[] { 1 }
            };
            Mocker.GetMock<IMovieGrabContextService>().Setup(service => service.GetContext(_item.DownloadId))
                .Returns(new MovieGrabContext { Series = _series, ParsedEpisodeInfo = parsed });
            var tracked = Subject.TrackDownload(_client, _item);
            if (edit)
            {
                tracked.State = TrackedDownloadState.ImportPending;
                Subject.Handle(new SeriesEditedEvent(_series, _series));
            }

            tracked.RemoteEpisode.ParsedEpisodeInfo.Should().BeSameAs(parsed);
            tracked.RemoteEpisode.Series.ForeignId.Should().Be("970001");
            tracked.RemoteEpisode.Episodes.Should().ContainSingle(episode => episode.SeriesId == 93 && episode.SeasonNumber == 1 && episode.EpisodeNumber == 1);
        }
    }
}
