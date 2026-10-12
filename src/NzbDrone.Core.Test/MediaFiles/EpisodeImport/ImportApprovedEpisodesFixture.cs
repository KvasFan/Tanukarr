using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ImportApprovedEpisodesFixture : CoreTest<ImportApprovedEpisodes>
    {
        private List<ImportDecision> _rejectedDecisions;
        private List<ImportDecision> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private ImportProgressService _importProgressService;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision>();
            _approvedDecisions = new List<ImportDecision>();

            var outputPath = @"C:\Test\Unsorted\TV\30.Rock.S01E01".AsOsAgnostic();

            var series = Builder<Series>.CreateNew()
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .With(s => s.Path = @"C:\Test\TV\30 Rock".AsOsAgnostic())
                                        .Build();

            var episodes = Builder<Episode>.CreateListOfSize(5)
                                           .Build();

            _rejectedDecisions.Add(new ImportDecision(new LocalEpisode(), new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")));
            _rejectedDecisions.Add(new ImportDecision(new LocalEpisode(), new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")));
            _rejectedDecisions.Add(new ImportDecision(new LocalEpisode(), new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")));
            _rejectedDecisions.ForEach(r => r.LocalEpisode.FileEpisodeInfo = new ParsedEpisodeInfo());

            foreach (var episode in episodes)
            {
                _approvedDecisions.Add(new ImportDecision(
                                           new LocalEpisode
                                               {
                                                   Series = series,
                                                   Episodes = new List<Episode> { episode },
                                                   Path = Path.Combine(series.Path, "30 Rock - S01E01 - Pilot.avi"),
                                                   Quality = new QualityModel(Quality.Bluray720p),
                                                   ReleaseGroup = "DRONE",
                                                   FileEpisodeInfo = new ParsedEpisodeInfo()
                                               }));
            }

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()))
                  .Returns(new EpisodeFileMoveResult());

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<EpisodeHistory>());

            _downloadClientItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.DownloadId = "ABC123")
                .With(d => d.DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 })
                .With(d => d.OutputPath = new OsPath(outputPath))
                .Build();

            _importProgressService = new ImportProgressService(Mocker.GetMock<IEventAggregator>().Object);
            Mocker.SetConstant<IImportProgressService>(_importProgressService);
        }

        private void GivenNewDownload()
        {
            _approvedDecisions.ForEach(a => a.LocalEpisode.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), Path.GetFileName(a.LocalEpisode.Path)));
        }

        private void GivenExistingFileOnDisk()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesWithRelativePath(It.IsAny<int>(), It.IsAny<string>()))
                  .Returns(new List<EpisodeFile>());
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<EpisodeFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            GivenExistingFileOnDisk();

            Subject.Import(_approvedDecisions, false).Should().HaveCount(5);
        }

        [Test]
        public void should_only_import_approved()
        {
            GivenExistingFileOnDisk();

            var all = new List<ImportDecision>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_episode_once()
        {
            GivenExistingFileOnDisk();

            var all = new List<ImportDecision>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision(_approvedDecisions.First().LocalEpisode));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), _approvedDecisions.First().LocalEpisode, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_EpisodeImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<EpisodeImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            GivenExistingFileOnDisk();

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), _approvedDecisions.First().LocalEpisode, false),
                          Times.Never());
        }

        [Test]
        public void should_import_larger_files_first()
        {
            GivenExistingFileOnDisk();

            var fileDecision = _approvedDecisions.First();
            fileDecision.LocalEpisode.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision(
                new LocalEpisode
                 {
                     Series = fileDecision.LocalEpisode.Series,
                     Episodes = new List<Episode> { fileDecision.LocalEpisode.Episodes.First() },
                     Path = @"C:\Test\TV\30 Rock\30 Rock - S01E01 - Pilot.avi".AsOsAgnostic(),
                     Quality = new QualityModel(Quality.Bluray720p),
                     Size = 80.Megabytes()
                 });

            var all = new List<ImportDecision>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.LocalEpisode.Size == fileDecision.LocalEpisode.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            GivenNewDownload();
            _downloadClientItem.Title = "30.Rock.S01E01";
            _downloadClientItem.CanMoveFiles = false;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), _approvedDecisions.First().LocalEpisode, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            GivenNewDownload();
            _downloadClientItem.Title = "30.Rock.S01E01";
            _downloadClientItem.CanMoveFiles = false;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), _approvedDecisions.First().LocalEpisode, false), Times.Once());
        }

        [Test]
        public void should_use_file_name_only_for_download_client_item_without_a_job_folder()
        {
            var fileName = "Series.Title.S01E01.720p.HDTV.x264-Sonarr.mkv";
            var path = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), fileName);

            _downloadClientItem.OutputPath = new OsPath(path);
            _approvedDecisions.First().LocalEpisode.Path = path;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == fileName)));
        }

        [Test]
        public void should_use_folder_and_file_name_only_for_download_client_item_with_a_job_folder()
        {
            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), name);

            _downloadClientItem.OutputPath = new OsPath(outputPath);
            _approvedDecisions.First().LocalEpisode.Path = Path.Combine(outputPath, name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_include_intermediate_folders_for_download_client_item_with_a_job_folder()
        {
            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), name);

            _downloadClientItem.OutputPath = new OsPath(outputPath);
            _approvedDecisions.First().LocalEpisode.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_use_folder_info_release_title_to_find_relative_path()
        {
            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), name);
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, null);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_get_relative_path_when_there_is_no_grandparent_windows()
        {
            WindowsOnly();

            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = @"C:\";
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, null);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_get_relative_path_when_there_is_no_grandparent_mono()
        {
            PosixOnly();

            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = "/";
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, null);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_get_relative_path_when_there_is_no_grandparent_for_UNC_path()
        {
            WindowsOnly();

            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = @"\\server\share";
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, null);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}.mkv")));
        }

        [Test]
        public void should_use_folder_info_release_title_to_find_relative_path_when_file_is_not_in_download_client_item_output_directory()
        {
            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), name);
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            _downloadClientItem.OutputPath = new OsPath(Path.Combine(@"C:\Test\Unsorted\TV-Other\".AsOsAgnostic(), name));
            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_delete_existing_metadata_files_with_the_same_path()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesWithRelativePath(It.IsAny<int>(), It.IsAny<string>()))
                  .Returns(Builder<EpisodeFile>.CreateListOfSize(1).BuildList());

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, false);

            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Delete(It.IsAny<EpisodeFile>(), DeleteMediaFileReason.ManualOverride), Times.Once());
        }

        [Test]
        public void should_use_folder_info_release_title_to_find_relative_path_when_download_client_item_has_an_empty_output_path()
        {
            var name = "Series.Title.S01E01.720p.HDTV.x264-Sonarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\TV\".AsOsAgnostic(), name);
            var localEpisode = _approvedDecisions.First().LocalEpisode;

            _downloadClientItem.OutputPath = default(OsPath);
            localEpisode.FolderEpisodeInfo = new ParsedEpisodeInfo { ReleaseTitle = name };
            localEpisode.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<EpisodeFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_include_scene_name_with_new_downloads()
        {
            var firstDecision = _approvedDecisions.First();
            firstDecision.LocalEpisode.SceneName = "Series.Title.S01E01.dvdrip-DRONE";

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeEpisodeFile(It.Is<EpisodeFile>(e => e.SceneName == firstDecision.LocalEpisode.SceneName), _approvedDecisions.First().LocalEpisode, false),
                      Times.Once());
        }

        [Test]
        public void should_register_all_season_pack_decisions_before_importing_first_file()
        {
            var decisions = BuildDistinctDecisions(4);
            _importProgressService.BeginBatch(7, "ABC123", decisions.SelectMany(d => d.LocalEpisode.Episodes).Select(e => e.Id));
            var progressDuringFirstTransfer = default(ImportProgress);

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()))
                .Callback(() => progressDuringFirstTransfer ??= _importProgressService.GetProgress(7, "ABC123"))
                .Returns(new EpisodeFileMoveResult());

            Subject.Import(decisions, true, _downloadClientItem);

            progressDuringFirstTransfer.Items.Should().HaveCount(4);
            progressDuringFirstTransfer.Items.Should().ContainSingle(i => i.State == ImportProgressState.Importing);
            progressDuringFirstTransfer.Items.Should().Contain(i => i.State == ImportProgressState.Waiting);
        }

        [Test]
        public void should_import_every_season_in_an_explicit_multi_season_batch()
        {
            var decisions = Enumerable.Range(1, 4).Select(season => new ImportDecision(new LocalEpisode
            {
                Series = _approvedDecisions.First().LocalEpisode.Series,
                Episodes = new List<Episode> { new Episode { Id = (season * 100) + 1, SeasonNumber = season, EpisodeNumber = 1 } },
                Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), $"Example Show S{season:00}E01.mkv"),
                Size = 1000,
                Quality = new QualityModel(Quality.Bluray720p),
                ReleaseGroup = "DRONE",
                FileEpisodeInfo = new ParsedEpisodeInfo()
            })).ToList();
            _importProgressService.BeginBatch(7, "ABC123", decisions.SelectMany(d => d.LocalEpisode.Episodes).Select(e => e.Id));

            Subject.Import(decisions, true, _downloadClientItem);

            var progress = _importProgressService.GetProgress(7, "ABC123");
            progress.TotalEpisodes.Should().Be(4);
            progress.ImportedEpisodes.Should().Be(4);
            progress.Items.Should().OnlyContain(i => i.State == ImportProgressState.Imported);
        }

        [Test]
        public void should_count_both_episodes_in_one_multi_episode_file()
        {
            var decision = new ImportDecision(new LocalEpisode
            {
                Series = _approvedDecisions.First().LocalEpisode.Series,
                Episodes = new List<Episode>
                {
                    new Episode { Id = 101, SeasonNumber = 1, EpisodeNumber = 1 },
                    new Episode { Id = 102, SeasonNumber = 1, EpisodeNumber = 2 }
                },
                Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "Example Show S01E01-E02.mkv"),
                Size = 1000,
                Quality = new QualityModel(Quality.Bluray720p),
                ReleaseGroup = "DRONE",
                FileEpisodeInfo = new ParsedEpisodeInfo()
            });
            _importProgressService.BeginBatch(7, "ABC123", new[] { 101, 102 });

            Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem);

            _importProgressService.GetProgress(7, "ABC123").ImportedEpisodes.Should().Be(2);
        }

        [Test]
        public void should_mark_imported_only_after_success_event_path_completes()
        {
            var decision = BuildDistinctDecisions(1).Single();
            _importProgressService.BeginBatch(7, "ABC123", new[] { decision.LocalEpisode.Episodes.Single().Id });
            var stateDuringEvent = ImportProgressState.Waiting;

            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<EpisodeImportedEvent>()))
                .Callback(() => stateDuringEvent = _importProgressService.GetProgress(7, "ABC123").Items.Single().State);

            Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem);

            stateDuringEvent.Should().Be(ImportProgressState.Importing);
            _importProgressService.GetProgress(7, "ABC123").Items.Single().State.Should().Be(ImportProgressState.Imported);
        }

        [Test]
        public void should_complete_replacement_progress_only_after_file_linkage_and_event()
        {
            var decision = BuildDistinctDecisions(1).Single();
            _importProgressService.BeginBatch(7, "ABC123", new[] { decision.LocalEpisode.Episodes.Single().Id });
            var stateDuringAdd = ImportProgressState.Waiting;
            var oldFile = new EpisodeFile { Id = 99 };

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()))
                .Returns(new EpisodeFileMoveResult
                {
                    OldFiles = new List<DeletedEpisodeFile> { new DeletedEpisodeFile(oldFile, @"C:\Recycle\old.mkv") }
                });
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.Add(It.IsAny<EpisodeFile>()))
                .Callback(() => stateDuringAdd = _importProgressService.GetProgress(7, "ABC123").Items.Single().State)
                .Returns(new EpisodeFile());
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<EpisodeImportedEvent>()))
                .Callback(() => _importProgressService.GetProgress(7, "ABC123").Items.Single().State.Should().Be(ImportProgressState.Importing));

            Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem);

            stateDuringAdd.Should().Be(ImportProgressState.Importing);
            _importProgressService.GetProgress(7, "ABC123").Items.Single().State.Should().Be(ImportProgressState.Imported);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_mark_destination_and_generic_import_failures_without_changing_result(bool destinationExists)
        {
            var decision = BuildDistinctDecisions(1).Single();
            _importProgressService.BeginBatch(7, "ABC123", new[] { decision.LocalEpisode.Episodes.Single().Id });

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()))
                .Throws(destinationExists ? new DestinationAlreadyExistsException("Exists") : new IOException("Failed"));

            var result = Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem).Single();

            ExceptionVerification.ExpectedWarns(1);

            result.Result.Should().Be(ImportResultType.Skipped);
            _importProgressService.GetProgress(7, "ABC123").Items.Single().State.Should().Be(ImportProgressState.Failed);
        }

        [TestCase(typeof(RootFolderNotFoundException), "Failed to import episode, Root folder missing.")]
        [TestCase(typeof(RecycleBinException), "Failed to import episode, unable to move existing file to the Recycle Bin.")]
        public void should_track_event_based_import_failures_without_changing_result(Type exceptionType, string expectedError)
        {
            var decision = BuildDistinctDecisions(1).Single();
            _importProgressService.BeginBatch(7, "ABC123", new[] { decision.LocalEpisode.Episodes.Single().Id });

            var exception = (Exception)Activator.CreateInstance(exceptionType, "Failed");
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()))
                .Throws(exception);

            var result = Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem).Single();

            ExceptionVerification.ExpectedWarns(1);

            result.Result.Should().Be(ImportResultType.Skipped);
            result.Errors.Should().Contain(expectedError);
            _importProgressService.GetProgress(7, "ABC123").Items.Single().State.Should().Be(ImportProgressState.Failed);
        }

        [Test]
        public void should_ignore_progress_service_failures_and_preserve_successful_import()
        {
            var decision = BuildDistinctDecisions(1).Single();
            var throwingProgress = new Mock<IImportProgressService>();
            throwingProgress.Setup(s => s.RegisterDecisions(It.IsAny<DownloadClientItem>(), It.IsAny<IEnumerable<ImportDecision>>())).Throws(new InvalidOperationException("telemetry"));
            throwingProgress.Setup(s => s.MarkImporting(It.IsAny<DownloadClientItem>(), It.IsAny<LocalEpisode>())).Throws(new InvalidOperationException("telemetry"));
            throwingProgress.Setup(s => s.MarkImported(It.IsAny<DownloadClientItem>(), It.IsAny<LocalEpisode>())).Throws(new InvalidOperationException("telemetry"));
            Mocker.SetConstant<IImportProgressService>(throwingProgress.Object);

            var result = Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem).Single();

            result.Result.Should().Be(ImportResultType.Imported);
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()), Times.Once());
        }

        [Test]
        public void should_track_rejected_decision_without_running_transfer()
        {
            var localEpisode = BuildDistinctDecisions(1).Single().LocalEpisode;
            var decision = new ImportDecision(localEpisode, new ImportRejection(ImportRejectionReason.Unknown, "Rejected exactly"));
            _importProgressService.BeginBatch(7, "ABC123", new[] { localEpisode.Episodes.Single().Id });

            Subject.Import(new List<ImportDecision> { decision }, true, _downloadClientItem);

            var item = _importProgressService.GetProgress(7, "ABC123").Items.Single();
            item.State.Should().Be(ImportProgressState.Rejected);
            item.Errors.Should().Equal("Rejected exactly");
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()), Times.Never());
        }

        private List<ImportDecision> BuildDistinctDecisions(int count)
        {
            var series = _approvedDecisions.First().LocalEpisode.Series;

            return Enumerable.Range(1, count).Select(number => new ImportDecision(new LocalEpisode
            {
                Series = series,
                Episodes = new List<Episode> { new Episode { Id = number, SeasonNumber = 1, EpisodeNumber = number } },
                Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), $"Example Show S01E{number:00}.mkv"),
                Size = 1000,
                Quality = new QualityModel(Quality.Bluray720p),
                ReleaseGroup = "DRONE",
                FileEpisodeInfo = new ParsedEpisodeInfo()
            })).ToList();
        }
    }
}
