using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using V3Mapper = Sonarr.Api.V3.Series.SeriesResourceMapper;
using V3Resource = Sonarr.Api.V3.Series.SeriesResource;
using V5Mapper = Sonarr.Api.V5.Series.SeriesResourceMapper;
using V5Resource = Sonarr.Api.V5.Series.SeriesResource;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class AnimeGroupingResourceFixture : TestBase
    {
        private readonly bool _useV3;

        public AnimeGroupingResourceFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [TestCase(AnimeGroupingMode.Automatic, "automatic")]
        [TestCase(AnimeGroupingMode.Manual, "manual")]
        [TestCase(AnimeGroupingMode.Disabled, "disabled")]
        public void should_expose_flat_group_fields_with_camel_case_modes(AnimeGroupingMode mode, string expectedMode)
        {
            var model = Grouped();
            model.AnimeGroupingMode = mode;
            var resource = Resource(model);
            var json = JObject.Parse(STJson.ToJson(resource));
            json["animeGroupingMode"].Value<string>().Should().Be(expectedMode);
            json["animeGroupId"].Value<string>().Should().Be("anilist:1");
            json["animeGroupTitle"].Value<string>().Should().Be("Example Anime");
            json["animeGroupSeasonNumber"].Value<int>().Should().Be(2);
            json["animeGroupFolder"].Value<string>().Should().Be("Existing Folder");
        }

        [Test]
        public void should_ignore_client_folder_values_on_add()
        {
            var resource = Resource(Grouped());
            Set(resource, "AnimeGroupFolder", "../../outside");
            Set(resource, "AnimeGroupMemberFolder", "../../outside-member");
            NewModel(resource).AnimeGroupFolder.Should().BeNull();
            NewModel(resource).AnimeGroupMemberFolder.Should().BeNull();
        }

        [Test]
        public void should_serialize_typed_movie_group_fields_and_keep_native_identity()
        {
            var movie = Movie();
            var json = JObject.Parse(STJson.ToJson(Resource(movie)));
            json["animeGroupMemberKind"].Value<string>().Should().Be("movie");
            json["animeGroupKind"].Value<string>().Should().Be("movieSaga");
            json["animeGroupMovieFolderStyle"].Value<string>().Should().Be("numbered");
            json["animeGroupMovieNumber"].Value<int>().Should().Be(2);
            json["animeGroupMemberFolder"].Value<string>().Should().Be("Film 02");
            if (_useV3)
            {
                json["tvdbId"].Value<int>().Should().Be(-2);
            }
            else
            {
                json["foreignId"].Value<string>().Should().Be("2");
            }

            movie.ForeignId.Should().Be("2");
            movie.MetadataSource.Should().Be(MetadataSourceType.AniList);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_movie_fields_when_legacy_put_omits_typed_membership(bool omitMode)
        {
            var movie = Movie();
            var resource = Resource(movie);
            if (omitMode)
            {
                Set(resource, "AnimeGroupingMode", null);
            }

            Set(resource, "AnimeGroupMemberKind", null);
            Set(resource, "AnimeGroupKind", null);
            Set(resource, "AnimeGroupMovieFolderStyle", null);
            Set(resource, "AnimeGroupMovieNumber", null);
            Set(resource, "AnimeGroupMemberFolder", "../../outside-member");
            UpdateModel(resource, movie);
            movie.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Movie);
            movie.AnimeGroupKind.Should().Be(AnimeGroupKind.MovieSaga);
            movie.AnimeGroupMovieFolderStyle.Should().Be(AnimeGroupMovieFolderStyle.Numbered);
            movie.AnimeGroupMovieNumber.Should().Be(2);
            movie.AnimeGroupSeasonNumber.Should().BeNull();
            movie.AnimeGroupMemberFolder.Should().Be("Film 02");
            movie.Path.Should().Be("/anime/Existing Folder/Film 02");
        }

        [Test]
        public void should_clear_the_opposite_ordinal_when_manually_changing_member_kind()
        {
            var existing = Grouped();
            var resource = Resource(existing);
            Set(resource, "AnimeGroupMemberKind", AnimeGroupMemberKind.Movie);
            Set(resource, "AnimeGroupMovieNumber", 3);
            Set(resource, "AnimeGroupMemberFolder", "Client Leaf");
            UpdateModel(resource, existing);
            existing.AnimeGroupSeasonNumber.Should().BeNull();
            existing.AnimeGroupMovieNumber.Should().Be(3);
            existing.AnimeGroupMemberFolder.Should().BeNull();
            existing.Path.Should().Be("/anime/Existing Folder/Season 02");
        }

        [TestCase(AnimeGroupingMode.Automatic)]
        [TestCase(AnimeGroupingMode.Manual)]
        public void should_preserve_group_fields_for_legacy_put_without_a_mode(AnimeGroupingMode mode)
        {
            var existing = Grouped();
            existing.AnimeGroupingMode = mode;
            var resource = Resource(existing);
            Set(resource, "AnimeGroupingMode", null);
            Set(resource, "AnimeGroupId", null);
            Set(resource, "AnimeGroupTitle", null);
            Set(resource, "AnimeGroupSeasonNumber", null);
            Set(resource, "AnimeGroupFolder", "../../outside");
            UpdateModel(resource, existing);
            existing.AnimeGroupingMode.Should().Be(mode);
            existing.AnimeGroupId.Should().Be("anilist:1");
            existing.AnimeGroupTitle.Should().Be("Example Anime");
            existing.AnimeGroupSeasonNumber.Should().Be(2);
            existing.AnimeGroupFolder.Should().Be("Existing Folder");
        }

        [Test]
        public void should_clear_manual_membership_when_switching_back_to_automatic()
        {
            var existing = Grouped();
            var resource = Resource(existing);
            Set(resource, "AnimeGroupingMode", AnimeGroupingMode.Automatic);
            UpdateModel(resource, existing);
            existing.AnimeGroupId.Should().BeNull();
            existing.AnimeGroupSeasonNumber.Should().BeNull();
            existing.Path.Should().Be("/anime/Existing Folder/Season 02");
        }

        [Test]
        public void should_clear_group_metadata_when_disabled_without_changing_the_path()
        {
            var existing = Grouped();
            var resource = Resource(existing);
            Set(resource, "AnimeGroupingMode", AnimeGroupingMode.Disabled);
            UpdateModel(resource, existing);
            existing.AnimeGroupId.Should().BeNull();
            existing.AnimeGroupFolder.Should().BeNull();
            existing.Path.Should().Be("/anime/Existing Folder/Season 02");
            existing.ForeignId.Should().Be("2");
        }

        [Test]
        public void should_default_add_requests_without_grouping_fields_to_automatic()
        {
            var resource = _useV3 ? (object)new V3Resource() : new V5Resource();
            NewModel(resource).AnimeGroupingMode.Should().Be(AnimeGroupingMode.Automatic);
        }

        private object Resource(NzbDrone.Core.Tv.Series series)
        {
            return _useV3 ? (object)V3Mapper.ToResource(series) : V5Mapper.ToResource(series);
        }

        private NzbDrone.Core.Tv.Series NewModel(object resource)
        {
            return _useV3 ? V3Mapper.ToModel((V3Resource)resource) : V5Mapper.ToModel((V5Resource)resource);
        }

        private void UpdateModel(object resource, NzbDrone.Core.Tv.Series existing)
        {
            if (_useV3)
            {
                V3Mapper.ToModel((V3Resource)resource, existing);
            }
            else
            {
                V5Mapper.ToModel((V5Resource)resource, existing);
            }
        }

        private static void Set(object resource, string name, object value)
        {
            resource.GetType().GetProperty(name).SetValue(resource, value);
        }

        private static NzbDrone.Core.Tv.Series Grouped()
        {
            return new NzbDrone.Core.Tv.Series
            {
                Id = 10, MetadataSource = MetadataSourceType.AniList, ForeignId = "2", TvdbId = -2,
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "anilist:1", AnimeGroupTitle = "Example Anime",
                AnimeGroupSeasonNumber = 2, AnimeGroupFolder = "Existing Folder", Path = "/anime/Existing Folder/Season 02"
            };
        }

        private static NzbDrone.Core.Tv.Series Movie()
        {
            var series = Grouped();
            series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            series.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            series.AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Numbered;
            series.AnimeGroupSeasonNumber = null;
            series.AnimeGroupMovieNumber = 2;
            series.AnimeGroupMemberFolder = "Film 02";
            series.Path = "/anime/Existing Folder/Film 02";
            return series;
        }
    }
}
