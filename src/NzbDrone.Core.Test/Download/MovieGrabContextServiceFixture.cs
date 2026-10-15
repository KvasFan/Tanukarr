using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class MovieGrabContextServiceFixture : CoreTest<MovieGrabContextService>
    {
        private const string PackTitle = "Generated Feature Collection.2019-2021.720p.WEB-DL.H264-Fixture";
        private List<Series> _movies;
        private List<EpisodeHistory> _history;
        private Dictionary<int, List<Episode>> _episodes;
        private DownloadClientItem _item;

        [SetUp]
        public void Setup()
        {
            _movies = Enumerable.Range(1, 2).Select(id => new Series
            {
                Id = id, Title = "Generated Feature " + (id == 1 ? "One" : "Two"), Year = id == 1 ? 2019 : 2021,
                ImdbId = "tt970000" + id, MediaType = MediaType.Movie, MetadataSource = MetadataSourceType.TmdbMovie,
                ForeignId = (970000 + id).ToString(), Runtime = 95 + id, Monitored = true,
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "manual:movie-pack",
                AnimeGroupTitle = "Generated Feature Collection", AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                AnimeGroupMovieNumber = id
            }).ToList();
            _episodes = _movies.ToDictionary(movie => movie.Id, movie => new List<Episode>
            {
                new Episode { Id = 100 + movie.Id, SeriesId = movie.Id, SeasonNumber = 1, EpisodeNumber = 1 }
            });
            _history = new List<EpisodeHistory>
            {
                new EpisodeHistory { EpisodeId = 101, SeriesId = 1, SourceTitle = PackTitle, EventType = EpisodeHistoryEventType.Grabbed }
            };
            _item = new DownloadClientItem { DownloadId = "generated-movie-pack", Title = PackTitle };
            Mocker.GetMock<IHistoryService>().Setup(service => service.FindByDownloadId(_item.DownloadId)).Returns(() => _history);
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(It.IsAny<int>()))
                .Returns<int>(id => _movies.SingleOrDefault(movie => movie.Id == id));
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetAllSeries()).Returns(() => _movies);
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodeBySeries(It.IsAny<int>()))
                .Returns<int>(id => _episodes.GetValueOrDefault(id, new List<Episode>()));
        }

        private LocalEpisode MemberFile(string path = "Generated Feature Two (2021) WEB-DL.mkv")
        {
            return new LocalEpisode
            {
                Path = path, Series = _movies[1], Episodes = _episodes[2], Release = new GrabbedReleaseInfo(_history)
            };
        }

        [Test]
        public void should_recover_native_single_feature_and_verified_pack_from_actual_grab_history()
        {
            var context = Subject.GetContext(_item.DownloadId);
            context.Series.Id.Should().Be(1);
            context.ParsedEpisodeInfo.IsMovieCollectionPack.Should().BeTrue();
            context.CollectionMovies.Select(movie => movie.Id).Should().Equal(1, 2);
            context.ParsedEpisodeInfo.EpisodeNumbers.Should().Equal(1);
            _history[0].SourceTitle = "Generated Feature One (2019) WEB-DL.mkv";
            context = Subject.GetContext(_item.DownloadId);
            context.ParsedEpisodeInfo.IsMovie.Should().BeTrue();
            context.ParsedEpisodeInfo.IsMovieCollectionPack.Should().BeFalse();
            context.CollectionMovies.Should().BeEmpty();
        }

        [Test]
        public void should_authorize_the_unique_physical_member_without_fabricating_grabbed_episode_rows()
        {
            var local = MemberFile();
            Subject.AllowsMember(local, _item).Should().BeTrue();
            Mocker.SetConstant<IMovieGrabContextService>(Subject);
            Mocker.Resolve<MatchesGrabSpecification>().IsSatisfiedBy(local, _item).Accepted.Should().BeTrue();
            local.Release.EpisodeIds.Should().Equal(101);
            _history.Should().ContainSingle(history => history.SeriesId == 1 && history.EpisodeId == 101);
        }

        [TestCase("Generated Feature Two (2020) WEB-DL.mkv")]
        [TestCase("Unrelated Feature (2021) WEB-DL.mkv")]
        [TestCase("Generated Feature Two S01E01.mkv")]
        [TestCase("Generated Feature Two (2021) imdb-tt9700099.mkv")]
        public void should_reject_a_wrong_or_ambiguous_physical_movie(string filename)
        {
            Subject.AllowsMember(MemberFile(filename), _item).Should().BeFalse();
        }

        [TestCase("noGrab")]
        [TestCase("foreignGrab")]
        [TestCase("wrongNativeEpisode")]
        [TestCase("movedGroup")]
        [TestCase("unmonitoredMember")]
        [TestCase("wrongRange")]
        [TestCase("extraNativeFeature")]
        [TestCase("foreignNativeFeature")]
        public void should_fail_closed_for_changed_or_inconsistent_native_grab_context(string defect)
        {
            switch (defect)
            {
                case "noGrab": _history[0].EventType = EpisodeHistoryEventType.DownloadFolderImported; break;
                case "foreignGrab": _history.Add(new EpisodeHistory { SeriesId = 2, EpisodeId = 102, SourceTitle = PackTitle, EventType = EpisodeHistoryEventType.Grabbed }); break;
                case "wrongNativeEpisode": _history[0].EpisodeId = 999; break;
                case "movedGroup": _movies[1].AnimeGroupId = "manual:unrelated"; break;
                case "unmonitoredMember": _movies[1].Monitored = false; break;
                case "wrongRange": _history[0].SourceTitle = PackTitle.Replace("2019-2021", "2018-2021"); break;
                case "extraNativeFeature": _episodes[1].Add(new Episode { Id = 103, SeriesId = 1, SeasonNumber = 1, EpisodeNumber = 2 }); break;
                case "foreignNativeFeature": _episodes[2][0].SeriesId = 999; break;
            }

            Subject.AllowsMember(MemberFile(), _item).Should().BeFalse();
        }

        [Test]
        public void should_reject_duplicate_owning_catalogues_for_the_same_physical_feature()
        {
            _movies.Add(new Series
            {
                Id = 3, Title = _movies[1].Title, Year = 2021, Runtime = 97, MediaType = MediaType.Movie, Monitored = true,
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = _movies[1].AnimeGroupId,
                AnimeGroupTitle = _movies[1].AnimeGroupTitle, AnimeGroupMemberKind = AnimeGroupMemberKind.Movie, AnimeGroupMovieNumber = 3
            });
            Subject.AllowsMember(MemberFile(), _item).Should().BeFalse();
        }

        [Test]
        public void should_fail_closed_when_the_grab_owner_was_deleted()
        {
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(1))
                .Throws(new ModelNotFoundException(typeof(Series), 1));
            Subject.GetContext(_item.DownloadId).Should().BeNull();
            Subject.AllowsMember(MemberFile(), _item).Should().BeFalse();
        }
    }
}
