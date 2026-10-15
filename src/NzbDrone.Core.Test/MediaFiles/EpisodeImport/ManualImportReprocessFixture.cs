using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ManualImportReprocessFixture : CoreTest<ManualImportService>
    {
        private Series _series;
        private List<Episode> _episodes;
        private string _path;
        private LocalEpisode _decisionEpisode;

        [SetUp]
        public void Setup()
        {
            _series = new Series
            {
                Id = 42,
                Title = "Example Show",
                Path = @"C:\library\Example Show".AsOsAgnostic(),
                OriginalLanguage = Language.English
            };
            _episodes = new List<Episode>
            {
                new Episode { Id = 51, SeriesId = 42, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = 99 }
            };
            _path = Path.Combine(_series.Path, "Example.Show.S01E01.mkv");
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(42)).Returns(_series);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>())).Returns(_episodes);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(It.IsAny<string>())).Returns(1234);
            Mocker.GetMock<ILocalEpisodeCustomFormatCalculationService>()
                .Setup(s => s.ParseEpisodeCustomFormats(It.IsAny<LocalEpisode>())).Returns(new List<CustomFormat>());
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((local, _) =>
                {
                    local.Episodes = new List<Episode>();
                    local.NumberingRejection = "Catalogue numbering is ambiguous";
                    return local;
                });
            Mocker.GetMock<IMakeImportDecision>().Setup(s => s.GetDecision(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalEpisode, DownloadClientItem>((local, _) =>
                {
                    _decisionEpisode = local;
                    return new ImportDecision(local);
                });
        }

        private ManualImportItem Reprocess()
        {
            return Subject.ReprocessItem(
                _path,
                null,
                42,
                1,
                new List<int> { 51 },
                "ManualGroup",
                new QualityModel(Quality.WEBDL720p),
                new List<Language> { Language.English },
                0,
                ReleaseType.SingleEpisode);
        }

        [Test]
        public void should_retain_existing_file_identity_for_exact_saved_native_path()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithRelativePath(42, "Example.Show.S01E01.mkv"))
                .Returns(new List<EpisodeFile> { new EpisodeFile { Id = 99, SeriesId = 42 } });

            var item = Reprocess();

            item.EpisodeFileId.Should().Be(99);
            item.Series.Id.Should().Be(42);
            item.SeasonNumber.Should().Be(1);
            item.Episodes.Should().Equal(_episodes);
            item.ReleaseGroup.Should().Be("ManualGroup");
            _decisionEpisode.ManualEpisodeMapping.Should().BeTrue();
            _decisionEpisode.NumberingRejection.Should().BeNull();
        }

        [Test]
        public void should_not_assign_a_file_identity_for_an_unmapped_native_path()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithRelativePath(42, It.IsAny<string>()))
                .Returns(new List<EpisodeFile>());

            Reprocess().EpisodeFileId.Should().BeNull();
        }

        [Test]
        public void should_not_guess_between_duplicate_saved_paths()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesWithRelativePath(42, It.IsAny<string>()))
                .Returns(new List<EpisodeFile> { new EpisodeFile { Id = 99 }, new EpisodeFile { Id = 100 } });

            Reprocess().EpisodeFileId.Should().BeNull();
        }

        [Test]
        public void should_not_reuse_an_episode_file_id_for_a_file_outside_the_native_series_path()
        {
            _path = @"C:\incoming\Example.Show.S01E01.mkv".AsOsAgnostic();

            Reprocess().EpisodeFileId.Should().BeNull();
            Mocker.GetMock<IMediaFileService>().Verify(s => s.GetFilesWithRelativePath(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
        }
    }
}
