using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.Grouping
{
    [TestFixture]
    public class CollectionMovieStyleInheritanceFixture : CoreTest
    {
        private AnimeGroupingService _service;
        private Series _canonical;

        [SetUp]
        public void Setup()
        {
            _canonical = Movie(10, 1, AnimeGroupingMode.Manual, AnimeGroupMovieFolderStyle.Title);
            _canonical.AnimeGroupFolder = "Saved collection folder";
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetAllSeries()).Returns(new List<Series> { _canonical });
            Mocker.GetMock<IBuildFileNames>().Setup(builder => builder.GetAnimeGroupMemberFolder(It.IsAny<Series>(), null))
                .Returns<Series, NamingConfig>((series, naming) => series.AnimeGroupMovieFolderStyle == AnimeGroupMovieFolderStyle.Title ? series.Title : "Film 02");
            var provider = new Mock<IAnimeRelationProvider>();
            provider.SetupGet(value => value.Source).Returns(MetadataSourceType.AniList);
            provider.Setup(value => value.GetAnimeRelations("100")).Returns(new AnimeRelationNode
            {
                ForeignId = "100", Title = "Movie one", Format = "MOVIE", SequelIds = new List<string> { "200" }
            });
            provider.Setup(value => value.GetAnimeRelations("200")).Returns(new AnimeRelationNode
            {
                ForeignId = "200", Title = "Movie two", Format = "MOVIE", PrequelIds = new List<string> { "100" }
            });
            _service = new AnimeGroupingService(new[] { provider.Object }, Mocker.GetMock<ISeriesService>().Object, Mocker.GetMock<IBuildFileNames>().Object, TestLogger);
        }

        [Test]
        public void should_apply_the_saved_collection_movie_style_to_a_new_automatic_member()
        {
            var added = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "200", Title = "Movie two", AnimeGroupingMode = AnimeGroupingMode.Automatic };
            _service.Apply(added);
            added.AnimeGroupId.Should().Be("anilist:100");
            added.AnimeGroupMovieFolderStyle.Should().Be(AnimeGroupMovieFolderStyle.Title);
            added.AnimeGroupMemberFolder.Should().Be("Movie two");
            added.AnimeGroupFolder.Should().Be("Saved collection folder");
        }

        [TestCase(0, AnimeGroupingMode.Manual)]
        [TestCase(20, AnimeGroupingMode.Manual)]
        [TestCase(20, AnimeGroupingMode.Automatic)]
        public void should_preserve_an_explicit_manual_or_saved_member_style(int id, AnimeGroupingMode mode)
        {
            var member = Movie(id, 2, mode, AnimeGroupMovieFolderStyle.Numbered);
            member.AnimeGroupFolder = "Saved collection folder";
            member.AnimeGroupMemberFolder = "Film 02";
            member.Path = "/generated/Saved collection folder/Film 02";
            _service.Apply(member);
            member.AnimeGroupMovieFolderStyle.Should().Be(AnimeGroupMovieFolderStyle.Numbered);
            member.AnimeGroupMemberFolder.Should().Be("Film 02");
            member.Path.Should().Be("/generated/Saved collection folder/Film 02");
        }

        private static Series Movie(int id, int number, AnimeGroupingMode mode, AnimeGroupMovieFolderStyle style)
        {
            return new Series
            {
                Id = id, Title = number == 1 ? "Movie one" : "Movie two", MetadataSource = MetadataSourceType.AniList,
                ForeignId = number == 1 ? "100" : "200", AnimeGroupingMode = mode, AnimeGroupId = "anilist:100",
                AnimeGroupTitle = "Saved collection", AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                AnimeGroupKind = AnimeGroupKind.MovieSaga, AnimeGroupMovieNumber = number, AnimeGroupMovieFolderStyle = style
            };
        }
    }
}
