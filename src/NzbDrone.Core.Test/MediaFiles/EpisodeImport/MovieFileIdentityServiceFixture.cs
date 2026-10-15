using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class MovieFileIdentityServiceFixture : CoreTest<MovieFileIdentityService>
    {
        private LocalEpisode _local;
        private EpisodeFile _file;
        private Episode _episode;

        [SetUp]
        public void Setup()
        {
            _local = new LocalEpisode
            {
                Series = new Series { Id = 10, MediaType = MediaType.Movie, Path = @"C:\Library\Feature".AsOsAgnostic() },
                Path = @"C:\Library\Feature\old-unparseable-file.mkv".AsOsAgnostic()
            };
            _file = new EpisodeFile { Id = 20, SeriesId = 10, RelativePath = "old-unparseable-file.mkv" };
            _episode = new Episode { Id = 30, SeriesId = 10, EpisodeFileId = 20, SeasonNumber = 1, EpisodeNumber = 1, ForeignId = "native-feature" };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesWithRelativePath(10, "old-unparseable-file.mkv"))
                .Returns(new List<EpisodeFile> { _file });
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodesByFileId(20))
                .Returns(new List<Episode> { _episode });
        }

        [Test]
        public void should_preserve_an_existing_same_series_file_mapping_without_reparsing_the_filename()
        {
            var mapped = Subject.GetPersistedEpisodes(_local);
            mapped.Should().ContainSingle().Which.Should().BeSameAs(_episode);
            _episode.ForeignId.Should().Be("native-feature");
            _episode.EpisodeFileId.Should().Be(20);
        }

        [TestCase(11, 20)]
        [TestCase(10, 21)]
        public void should_reject_foreign_series_or_file_associations(int seriesId, int episodeFileId)
        {
            _episode.SeriesId = seriesId;
            _episode.EpisodeFileId = episodeFileId;
            Subject.GetPersistedEpisodes(_local).Should().BeNull();
        }

        [Test]
        public void should_reject_a_same_named_download_outside_the_owned_library()
        {
            _local.ExistingFile = true;
            _local.Path = @"C:\Downloads\Feature\old-unparseable-file.mkv".AsOsAgnostic();
            Subject.GetPersistedEpisodes(_local).Should().BeNull();
            Mocker.GetMock<IMediaFileService>().Verify(service => service.GetFilesWithRelativePath(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_reject_duplicate_persisted_file_or_episode_associations()
        {
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesWithRelativePath(10, "old-unparseable-file.mkv"))
                .Returns(new List<EpisodeFile> { _file, _file });
            Subject.GetPersistedEpisodes(_local).Should().BeNull();
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesWithRelativePath(10, "old-unparseable-file.mkv"))
                .Returns(new List<EpisodeFile> { _file });
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodesByFileId(20))
                .Returns(new List<Episode> { _episode, _episode });
            Subject.GetPersistedEpisodes(_local).Should().BeNull();
        }
    }
}
