using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using CoreSeries = NzbDrone.Core.Tv.Series;
using V3Mapper = Sonarr.Api.V3.Series.SeriesResourceMapper;
using V5Mapper = Sonarr.Api.V5.Series.SeriesResourceMapper;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class CollectionEditingResourceFixture : TestBase
    {
        private readonly bool _useV3;
        private List<CoreSeries> _members;

        public CollectionEditingResourceFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            _members = new List<CoreSeries>
            {
                Member(42, 1), Member(43, 2)
            };
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(It.IsAny<IEnumerable<int>>())).Returns(() => _members);
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetAllSeries()).Returns(() => _members);
            Mocker.GetMock<ISeriesService>().Setup(service => service.UpdateSeries(It.IsAny<List<CoreSeries>>(), It.IsAny<bool>()))
                .Returns<List<CoreSeries>, bool>((members, move) => members);
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.Exists(1)).Returns(true);
        }

        [Test]
        public void should_expose_a_native_collection_title_separately_from_the_catalogue_title()
        {
            var native = NativeSeries();
            native.CollectionTitle = "Display collection";
            object resource = _useV3 ? V3Mapper.ToResource(native) : V5Mapper.ToResource(native);
            var json = JObject.Parse(STJson.ToJson(resource));
            json["title"].Value<string>().Should().Be("Native TV title");
            json["collectionTitle"].Value<string>().Should().Be("Display collection");
        }

        [Test]
        public void should_ignore_injected_display_titles_on_the_single_series_add_and_update_resource()
        {
            var native = NativeSeries();
            native.CollectionTitle = "Saved display name";
            if (_useV3)
            {
                var resource = V3Mapper.ToResource(native);
                resource.CollectionTitle = "Injected display name";
                V3Mapper.ToModel(resource).CollectionTitle.Should().BeNull();
                V3Mapper.ToModel(resource, native).CollectionTitle.Should().Be("Saved display name");
            }
            else
            {
                var resource = V5Mapper.ToResource(native);
                resource.CollectionTitle = "Injected display name";
                V5Mapper.ToModel(resource).CollectionTitle.Should().BeNull();
                V5Mapper.ToModel(resource, native).CollectionTitle.Should().Be("Saved display name");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_emit_an_explicit_null_display_title_so_a_reset_clears_cached_names(bool useNewtonsoft)
        {
            var native = NativeSeries();
            object resource = _useV3 ? V3Mapper.ToResource(native) : V5Mapper.ToResource(native);
            var json = JObject.Parse(useNewtonsoft ? Json.ToJson(resource) : STJson.ToJson(resource));
            json.ContainsKey("collectionTitle").Should().BeTrue();
            json["collectionTitle"].Type.Should().Be(JTokenType.Null);
            json["title"].Value<string>().Should().Be("Native TV title");
        }

        [Test]
        public void should_save_complete_linked_collection_settings_without_requesting_a_file_move()
        {
            SaveGroup("Corrected collection");
            _members.Should().OnlyContain(member => member.AnimeGroupTitle == "Corrected collection" && member.AnimeGroupingMode == AnimeGroupingMode.Manual);
            _members[0].Path.Should().Be("/generated/Original/Season 1");
            _members[1].Path.Should().Be("/generated/Original/Season 2");
            Mocker.GetMock<ISeriesService>().Verify(service => service.UpdateSeries(_members, false), Times.Once());
        }

        [Test]
        public void should_reject_partial_selection_before_persisting_or_changing_existing_members()
        {
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(It.IsAny<IEnumerable<int>>())).Returns(new List<CoreSeries> { _members[0] });
            Action save = () => SaveGroup("Changed");
            save.Should().Throw<ValidationException>();
            _members.Should().OnlyContain(member => member.AnimeGroupTitle == "Original" && member.AnimeGroupingMode == AnimeGroupingMode.Automatic);
            Mocker.GetMock<ISeriesService>().Verify(service => service.UpdateSeries(It.IsAny<List<CoreSeries>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_save_a_native_collection_display_name_through_bulk_edit_without_a_group_or_path_change()
        {
            _members = new List<CoreSeries> { NativeSeries() };
            if (_useV3)
            {
                Mocker.Resolve<Sonarr.Api.V3.Series.SeriesEditorController>().SaveAll(new Sonarr.Api.V3.Series.SeriesEditorResource
                {
                    SeriesIds = new List<int> { 42 }, CollectionTitle = "  Native collection  "
                });
            }
            else
            {
                Mocker.Resolve<Sonarr.Api.V5.Series.SeriesEditorController>().SaveAll(new Sonarr.Api.V5.Series.SeriesEditorResource
                {
                    SeriesIds = new List<int> { 42 }, CollectionTitle = "  Native collection  "
                });
            }

            _members[0].CollectionTitle.Should().Be("Native collection");
            _members[0].Title.Should().Be("Native TV title");
            _members[0].Path.Should().Be("/generated/Native TV title");
            _members[0].AnimeGroupId.Should().BeNull();
            Mocker.GetMock<ISeriesService>().Verify(service => service.GetAllSeries(), Times.Never());
            Mocker.GetMock<ISeriesService>().Verify(service => service.UpdateSeries(_members, false), Times.Once());
        }

        [Test]
        public void should_keep_collection_settings_omitted_by_older_bulk_edit_clients()
        {
            _members = new List<CoreSeries> { NativeSeries() };
            _members[0].CollectionTitle = "Saved display name";
            if (_useV3)
            {
                var resource = JsonConvert.DeserializeObject<Sonarr.Api.V3.Series.SeriesEditorResource>("{\"seriesIds\":[42],\"monitored\":true}", Json.GetSerializerSettings());
                resource.CollectionTitle.Should().BeNull();
                resource.AnimeGroupKind.Should().BeNull();
                Mocker.Resolve<Sonarr.Api.V3.Series.SeriesEditorController>().SaveAll(resource);
            }
            else
            {
                var resource = JsonConvert.DeserializeObject<Sonarr.Api.V5.Series.SeriesEditorResource>("{\"seriesIds\":[42],\"monitored\":true}", Json.GetSerializerSettings());
                resource.CollectionTitle.Should().BeNull();
                resource.AnimeGroupKind.Should().BeNull();
                Mocker.Resolve<Sonarr.Api.V5.Series.SeriesEditorController>().SaveAll(resource);
            }

            _members[0].CollectionTitle.Should().Be("Saved display name");
        }

        private void SaveGroup(string title)
        {
            if (_useV3)
            {
                Mocker.Resolve<Sonarr.Api.V3.Series.SeriesEditorController>().SaveAll(new Sonarr.Api.V3.Series.SeriesEditorResource
                {
                    SeriesIds = new List<int> { 42, 43 }, AnimeGroupTitle = title, AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Title
                });
            }
            else
            {
                Mocker.Resolve<Sonarr.Api.V5.Series.SeriesEditorController>().SaveAll(new Sonarr.Api.V5.Series.SeriesEditorResource
                {
                    SeriesIds = new List<int> { 42, 43 }, AnimeGroupTitle = title, AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Title
                });
            }
        }

        private static CoreSeries NativeSeries()
        {
            return new CoreSeries { Id = 42, Title = "Native TV title", Path = "/generated/Native TV title", QualityProfileId = 1, Language = Language.English };
        }

        private static CoreSeries Member(int id, int season)
        {
            return new CoreSeries
            {
                Id = id, Title = "Native entry " + id, QualityProfileId = 1, Language = Language.English,
                AnimeGroupingMode = AnimeGroupingMode.Automatic, AnimeGroupId = "anilist:100", AnimeGroupTitle = "Original",
                AnimeGroupSeasonNumber = season, Path = "/generated/Original/Season " + season
            };
        }
    }
}
