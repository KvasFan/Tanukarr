using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    [TestFixture]
    public class RetainedEpisodesFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _download;
        private List<Episode> _episodes;
        private List<EpisodeFile> _files;
        private string _directory;

        [SetUp]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_directory);
            _episodes = Enumerable.Range(1, 3).Select(id => new Episode
            {
                Id = id, SeriesId = 42, SeasonNumber = id, EpisodeNumber = 1, EpisodeFileId = id
            }).ToList();
            _files = _episodes.Select(e => new EpisodeFile
            {
                Id = e.Id, SeriesId = 42, RelativePath = e.Id + ".mkv", Path = Path.Combine(_directory, e.Id + ".mkv")
            }).ToList();
            foreach (var file in _files)
            {
                File.WriteAllBytes(file.Path, new byte[] { 1 });
            }

            _download = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { DownloadId = "torrent", Title = "Show.S01-S03" },
                RemoteEpisode = new RemoteEpisode { Series = new Series { Id = 42, Path = _directory }, Episodes = _episodes },
                State = TrackedDownloadState.Importing
            };
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("torrent"))
                .Returns(new List<EpisodeHistory>());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>()))
                .Returns(_episodes);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFiles(It.IsAny<IEnumerable<int>>()))
                .Returns(_files);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.OpenReadStream(It.IsAny<string>()))
                .Returns<string>(path => File.OpenRead(path));
        }

        [TearDown]
        public void Cleanup()
        {
            Directory.Delete(_directory, true);
        }

        private ImportResult Result(int id, params ImportRejectionReason[] reasons)
        {
            var local = new LocalEpisode
            {
                Series = _download.RemoteEpisode.Series,
                Episodes = new List<Episode> { _episodes.Single(e => e.Id == id) }
            };
            var decision = new ImportDecision(local, reasons.Select(r => new ImportRejection(r, "reason")).ToArray());
            return reasons.Length == 0 ? new ImportResult(decision, _files.Single(f => f.Id == id)) :
                new ImportResult(decision, "rejected");
        }

        private List<ImportResult> MixedResults(ImportRejectionReason reason = ImportRejectionReason.NotQualityUpgrade)
        {
            return new List<ImportResult> { Result(1), Result(2, reason), Result(3, reason) };
        }

        private void AssertCompletion(List<ImportResult> results, bool expected)
        {
            Subject.VerifyImport(_download, results).Should().Be(expected);
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.IsAny<DownloadCompletedEvent>()),
                expected ? Times.Once() : Times.Never());
            if (expected)
            {
                _download.State.Should().Be(TrackedDownloadState.Imported);
            }
        }

        [TestCase(ImportRejectionReason.NotQualityUpgrade)]
        [TestCase(ImportRejectionReason.NotRevisionUpgrade)]
        [TestCase(ImportRejectionReason.NotCustomFormatUpgrade)]
        public void should_complete_mixed_imports_and_preferred_existing_files(ImportRejectionReason reason)
        {
            AssertCompletion(MixedResults(reason), true);
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.Is<DownloadCompletedEvent>(e =>
                e.EpisodeFiles.Count == 1 && e.EpisodeFiles[0].Id == 1)),
                Times.Once());
        }

        [Test]
        public void should_complete_entirely_redundant_pack_without_fake_imports()
        {
            AssertCompletion(_episodes.Select(e => Result(e.Id, ImportRejectionReason.NotQualityUpgrade)).ToList(), true);
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.Is<DownloadCompletedEvent>(e =>
                e.EpisodeFiles.Count == 0)),
                Times.Once());
        }

        [Test]
        public void should_combine_previous_imports_with_retained_versions()
        {
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("torrent"))
                .Returns(new List<EpisodeHistory>
                {
                    new EpisodeHistory { EpisodeId = 1, EventType = EpisodeHistoryEventType.DownloadFolderImported }
                });
            AssertCompletion(new List<ImportResult>
            {
                Result(1, ImportRejectionReason.EpisodeAlreadyImported),
                Result(2, ImportRejectionReason.NotCustomFormatUpgrade),
                Result(3, ImportRejectionReason.NotRevisionUpgrade)
            },
                true);
        }

        [TestCase(ImportRejectionReason.Error)]
        [TestCase(ImportRejectionReason.UnableToParse)]
        [TestCase(ImportRejectionReason.UnverifiedSceneMapping)]
        [TestCase(ImportRejectionReason.ExistingFileHasMoreEpisodes)]
        [TestCase(ImportRejectionReason.NotCustomFormatUpgradeAfterRename)]
        [TestCase(ImportRejectionReason.UnmonitoredEpisode)]
        [TestCase(ImportRejectionReason.DangerousFile)]
        public void should_keep_other_rejections_blocked(ImportRejectionReason reason)
        {
            AssertCompletion(MixedResults(reason), false);
        }

        [Test]
        public void should_keep_multiple_rejection_with_error_blocked()
        {
            AssertCompletion(new List<ImportResult>
            {
                Result(1), Result(2, ImportRejectionReason.NotQualityUpgrade, ImportRejectionReason.Error),
                Result(3, ImportRejectionReason.NotQualityUpgrade)
            },
                false);
        }

        [Test]
        public void should_not_count_unhandled_episode_even_if_library_has_it()
        {
            AssertCompletion(new List<ImportResult> { Result(1), Result(2, ImportRejectionReason.NotQualityUpgrade) }, false);
        }

        [Test]
        public void should_not_count_duplicate_episode_for_missing_expected_id()
        {
            AssertCompletion(new List<ImportResult>
            {
                Result(1), Result(2, ImportRejectionReason.NotQualityUpgrade), Result(2, ImportRejectionReason.NotQualityUpgrade)
            },
                false);
        }

        [Test]
        public void should_not_count_missing_retained_file()
        {
            File.Delete(_files[1].Path);
            AssertCompletion(MixedResults(), false);
        }

        [Test]
        public void should_not_count_empty_retained_file()
        {
            File.WriteAllBytes(_files[1].Path, Array.Empty<byte>());
            AssertCompletion(MixedResults(), false);
        }

        [Test]
        public void should_not_count_unreadable_retained_file()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.OpenReadStream(_files[1].Path))
                .Throws(new UnauthorizedAccessException());
            AssertCompletion(MixedResults(), false);
        }

        [Test]
        public void should_not_count_missing_file_record()
        {
            _files.RemoveAt(1);
            AssertCompletion(MixedResults(), false);
        }

        [Test]
        public void should_not_count_wrong_series_file_record()
        {
            _files[1].SeriesId = 99;
            AssertCompletion(MixedResults(), false);
        }

        [Test]
        public void should_not_count_wrong_series_candidate()
        {
            var results = MixedResults();
            results[1].ImportDecision.LocalEpisode.Series = new Series { Id = 99 };
            AssertCompletion(results, false);
        }

        [Test]
        public void should_not_count_stale_import_history_after_failure()
        {
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("torrent"))
                .Returns(new List<EpisodeHistory>
                {
                    new EpisodeHistory { EpisodeId = 1, Date = DateTime.UtcNow, EventType = EpisodeHistoryEventType.DownloadFailed },
                    new EpisodeHistory { EpisodeId = 1, Date = DateTime.UtcNow.AddDays(-1), EventType = EpisodeHistoryEventType.DownloadFolderImported }
                });
            AssertCompletion(new List<ImportResult>
            {
                Result(1, ImportRejectionReason.EpisodeAlreadyImported),
                Result(2, ImportRejectionReason.NotQualityUpgrade), Result(3, ImportRejectionReason.NotQualityUpgrade)
            },
                false);
        }

        [Test]
        public void should_require_all_episodes_in_retained_multi_episode_file()
        {
            var results = new List<ImportResult> { Result(1), Result(2, ImportRejectionReason.NotQualityUpgrade) };
            results[1].ImportDecision.LocalEpisode.Episodes.Add(_episodes[2]);
            AssertCompletion(results, true);
        }

        [Test]
        public void should_block_multi_episode_retention_when_one_association_is_missing()
        {
            var results = new List<ImportResult> { Result(1), Result(2, ImportRejectionReason.NotQualityUpgrade) };
            results[1].ImportDecision.LocalEpisode.Episodes.Add(_episodes[2]);
            _episodes[2].EpisodeFileId = 0;
            AssertCompletion(results, false);
        }
    }
}
