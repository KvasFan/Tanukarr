using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class RefreshEpisodeProviderIdentityFixture : DbTest<RefreshEpisodeService, Episode>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = new Series
            {
                Id = 1, Title = "Generated native episode identity", MetadataSource = MetadataSourceType.TvMaze,
                Status = SeriesStatusType.Continuing, AddOptions = new AddSeriesOptions(),
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } }
            };
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodeBySeries(_series.Id))
                .Returns(() => Storage.All().Where(episode => episode.SeriesId == _series.Id).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(service => service.InsertMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(episodes => Storage.InsertMany(episodes));
            Mocker.GetMock<IEpisodeService>().Setup(service => service.UpdateMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(episodes => Storage.UpdateMany(episodes));
            Mocker.GetMock<IEpisodeService>().Setup(service => service.DeleteMany(It.IsAny<List<Episode>>()))
                .Callback<List<Episode>>(episodes => Storage.DeleteMany(episodes));
        }

        [TestCase(MetadataSourceType.TvMaze)]
        [TestCase(MetadataSourceType.Tmdb)]
        [TestCase(MetadataSourceType.AniList)]
        [TestCase(MetadataSourceType.MyAnimeList)]
        public void should_persist_native_episode_identity_on_initial_refresh(MetadataSourceType source)
        {
            _series.MetadataSource = source;

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("native-31", 1) });

            StoredModel.ForeignId.Should().Be("native-31");
            StoredModel.TvdbId.Should().Be(0);
        }

        [Test]
        public void should_keep_database_rows_files_and_monitoring_attached_to_unique_native_ids_when_numbers_swap()
        {
            var first = Stored("native-a", 1, 101);
            first.Monitored = false;
            Storage.Update(first);
            var second = Stored("native-b", 2, 102);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("native-b", 1), Remote("native-a", 2) });

            var rows = Storage.All().ToDictionary(episode => episode.ForeignId);
            rows["native-a"].Id.Should().Be(first.Id);
            rows["native-a"].EpisodeFileId.Should().Be(101);
            rows["native-a"].EpisodeNumber.Should().Be(2);
            rows["native-a"].Monitored.Should().BeFalse();
            rows["native-b"].Id.Should().Be(second.Id);
            rows["native-b"].EpisodeFileId.Should().Be(102);
            rows["native-b"].EpisodeNumber.Should().Be(1);
        }

        [Test]
        public void should_reserve_native_matches_before_matching_an_earlier_legacy_slot()
        {
            var existing = Stored("native-a", 1, 101);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote(null, 1), Remote("native-a", 2) });

            var rows = Storage.All();
            rows.Single(episode => episode.ForeignId == "native-a").Id.Should().Be(existing.Id);
            rows.Single(episode => episode.ForeignId == "native-a").EpisodeFileId.Should().Be(101);
            rows.Single(episode => episode.EpisodeNumber == 1).EpisodeFileId.Should().Be(0);
        }

        [Test]
        public void should_backfill_a_missing_native_id_by_number_without_detaching_an_existing_file()
        {
            var existing = Stored(null, 1, 101);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("native-a", 1) });

            StoredModel.Id.Should().Be(existing.Id);
            StoredModel.ForeignId.Should().Be("native-a");
            StoredModel.EpisodeFileId.Should().Be(101);
        }

        [Test]
        public void should_preserve_a_saved_native_id_when_the_same_numbered_response_omits_it()
        {
            var existing = Stored("native-a", 1, 101);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote(null, 1) });

            StoredModel.Id.Should().Be(existing.Id);
            StoredModel.ForeignId.Should().Be("native-a");
            StoredModel.EpisodeFileId.Should().Be(101);
        }

        [Test]
        public void should_not_attach_a_different_known_episode_file_to_a_reused_number()
        {
            Stored("removed-native", 1, 101);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("new-native", 1) });

            StoredModel.ForeignId.Should().Be("new-native");
            StoredModel.EpisodeFileId.Should().Be(0);
        }

        [Test]
        public void should_keep_numbered_rows_when_duplicate_remote_ids_cannot_establish_a_unique_match()
        {
            var first = Stored("duplicate", 1, 101);
            var second = Stored("duplicate", 2, 102);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("duplicate", 1), Remote("duplicate", 2) });

            var rows = Storage.All();
            rows.Single(episode => episode.Id == first.Id).EpisodeFileId.Should().Be(101);
            rows.Single(episode => episode.Id == second.Id).EpisodeFileId.Should().Be(102);
        }

        [Test]
        public void should_not_guess_a_renumbered_row_from_duplicate_existing_native_ids()
        {
            Stored("duplicate", 1, 101);
            Stored("duplicate", 2, 102);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("duplicate", 3) });

            StoredModel.EpisodeNumber.Should().Be(3);
            StoredModel.EpisodeFileId.Should().Be(0);
        }

        [Test]
        public void should_never_match_a_native_id_from_another_series()
        {
            var other = Remote("native-a", 1);
            other.SeriesId = 2;
            other.EpisodeFileId = 202;
            Storage.Insert(other);

            Subject.RefreshEpisodeInfo(_series, new[] { Remote("native-a", 1) });

            Storage.All().Single(episode => episode.SeriesId == 2).EpisodeFileId.Should().Be(202);
            Storage.All().Single(episode => episode.SeriesId == 1).EpisodeFileId.Should().Be(0);
        }

        private Episode Stored(string nativeId, int number, int fileId)
        {
            var episode = Remote(nativeId, number);
            episode.EpisodeFileId = fileId;
            return Storage.Insert(episode);
        }

        private static Episode Remote(string nativeId, int number)
        {
            return new Episode
            {
                SeriesId = 1, ForeignId = nativeId, SeasonNumber = 1, EpisodeNumber = number,
                Title = "Generated episode " + number, AirDateUtc = DateTime.UtcNow.AddDays(-1), Monitored = true
            };
        }
    }
}
