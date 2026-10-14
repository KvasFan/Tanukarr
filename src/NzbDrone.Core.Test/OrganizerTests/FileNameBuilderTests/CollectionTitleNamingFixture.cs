using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class CollectionTitleNamingFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private NamingConfig _naming;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Title = "Native TV title", CollectionTitle = "Display collection", Year = 2020, Path = "/generated/Native TV title" };
            _naming = NamingConfig.Default;
            _naming.SeriesFolderFormat = "{Series Title} ({Series Year})";
            _naming.RenameEpisodes = true;
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(_naming);
            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat>());
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(It.IsAny<Quality>()))
                .Returns<Quality>(quality => Quality.DefaultQualityDefinitions.First(definition => definition.Quality == quality));
        }

        [Test]
        public void should_use_the_collection_display_name_for_future_series_folders_only()
        {
            Subject.GetSeriesFolder(_series).Should().Be("Display collection (2020)");
            _series.Title.Should().Be("Native TV title");
            _series.Path.Should().Be("/generated/Native TV title");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_use_the_native_title_when_no_display_name_is_set(string displayName)
        {
            _series.CollectionTitle = displayName;
            Subject.GetSeriesFolder(_series).Should().Be("Native TV title (2020)");
        }

        [Test]
        public void should_preserve_the_explicit_title_overload_for_existing_callers()
        {
            Subject.GetSeriesFolder(_series, "Explicit folder title", _naming).Should().Be("Explicit folder title (2020)");
        }

        [Test]
        public void should_keep_the_native_catalogue_title_in_episode_file_names()
        {
            _naming.StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00}";
            Subject.BuildFileName(new List<Episode> { new Episode { SeasonNumber = 2, EpisodeNumber = 3 } }, _series, new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p) })
                .Should().Be("Native TV title - S02E03");
            _series.CollectionTitle.Should().Be("Display collection");
        }
    }
}
