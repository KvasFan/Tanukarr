using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.LibraryTools;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ImportDecisionMakerFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;
        private LocalEpisode _localEpisode;
        private Series _series;
        private QualityModel _quality;

        private Mock<IImportDecisionEngineSpecification> _pass1;
        private Mock<IImportDecisionEngineSpecification> _pass2;
        private Mock<IImportDecisionEngineSpecification> _pass3;

        private Mock<IImportDecisionEngineSpecification> _fail1;
        private Mock<IImportDecisionEngineSpecification> _fail2;
        private Mock<IImportDecisionEngineSpecification> _fail3;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IImportDecisionEngineSpecification>();
            _pass2 = new Mock<IImportDecisionEngineSpecification>();
            _pass3 = new Mock<IImportDecisionEngineSpecification>();

            _fail1 = new Mock<IImportDecisionEngineSpecification>();
            _fail2 = new Mock<IImportDecisionEngineSpecification>();
            _fail3 = new Mock<IImportDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail3"));

            _series = Builder<Series>.CreateNew()
                                     .With(e => e.Path = @"C:\Test\Series".AsOsAgnostic())
                                     .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                     .Build();

            _quality = new QualityModel(Quality.DVD);

            _localEpisode = new LocalEpisode
            {
                Series = _series,
                Quality = _quality,
                Languages = new List<Language> { Language.Spanish },
                Episodes = new List<Episode> { new Episode() },
                Path = @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.Spanish.XviD-OSiTV.avi"
            };

            GivenVideoFiles(new List<string> { @"C:\Test\Unsorted\The.Office.S03E115.DVDRip.Spanish.XviD-OSiTV.avi".AsOsAgnostic() });
        }

        private void GivenSpecifications(params Mock<IImportDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant(mocks.Select(c => c.Object));
        }

        private void GivenVideoFiles(IEnumerable<string> videoFiles)
        {
            _videoFiles = videoFiles.ToList();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Series>()))
                  .Returns(_videoFiles);
        }

        private void GivenAugmentationSuccess()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalEpisode, DownloadClientItem>((localEpisode, downloadClientItem) =>
                  {
                      localEpisode.Episodes = _localEpisode.Episodes;
                  });
        }

        private List<Episode> GetMappedMultiSeasonEpisodes(int? seriesId = null)
        {
            return Enumerable.Range(1, 7)
                .Select(season => new Episode
                {
                    Id = (season * 100) + 1,
                    SeriesId = seriesId ?? _series.Id,
                    SeasonNumber = season,
                    EpisodeNumber = 1
                })
                .ToList();
        }

        private List<ImportDecision> GetLegacyMultiSeasonDecisions(ParsedEpisodeInfo trackedEpisodeInfo,
                                                                   List<Episode> trackedEpisodes,
                                                                   Series trackedSeries = null,
                                                                   int[] historicalEpisodeIds = null,
                                                                   string downloadId = "legacy-multi-season",
                                                                   string trackedDownloadId = "legacy-multi-season",
                                                                   bool includeRemoteEpisode = true,
                                                                   int? overriddenMiddleEpisodeId = null,
                                                                   bool includeTrackedDownload = true)
        {
            const string releaseTitle = "Example.Show.S01-S07.1080p.WEB-DL";
            var episodes = GetMappedMultiSeasonEpisodes();
            var videoFiles = episodes
                .Select(episode => $@"C:\Test\Unsorted\Example.Show.S{episode.SeasonNumber:00}E01.mkv".AsOsAgnostic())
                .ToList();
            var grabbedHistories = (historicalEpisodeIds ?? new[] { 101, 701 })
                .Select(episodeId => new EpisodeHistory
                {
                    EpisodeId = episodeId,
                    EventType = EpisodeHistoryEventType.Grabbed,
                    SourceTitle = releaseTitle
                })
                .ToList();
            var downloadClientItem = new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = releaseTitle
            };
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    DownloadId = trackedDownloadId,
                    Title = releaseTitle
                },
                RemoteEpisode = includeRemoteEpisode
                    ? new RemoteEpisode
                    {
                        Series = trackedSeries ?? _series,
                        ParsedEpisodeInfo = trackedEpisodeInfo,
                        Episodes = trackedEpisodes
                    }
                    : null
            };

            GivenVideoFiles(videoFiles);

            Mocker.GetMock<IAggregationService>()
                .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), downloadClientItem))
                .Callback<LocalEpisode, DownloadClientItem>((localEpisode, _) =>
                {
                    var episode = episodes.Single(item => item.SeasonNumber == localEpisode.FileEpisodeInfo.SeasonNumber);

                    if (overriddenMiddleEpisodeId.HasValue && localEpisode.FileEpisodeInfo.SeasonNumber == 4)
                    {
                        episode = new Episode
                        {
                            Id = overriddenMiddleEpisodeId.Value,
                            SeriesId = _series.Id,
                            SeasonNumber = 4,
                            EpisodeNumber = 1
                        };
                    }

                    localEpisode.Episodes = new List<Episode>
                    {
                        episode
                    };
                    localEpisode.Release = new GrabbedReleaseInfo(grabbedHistories);
                    localEpisode.Quality = _quality;
                    localEpisode.Languages = new List<Language> { Language.English };
                });

            Mocker.GetMock<ITrackedDownloadService>()
                .Setup(s => s.Find(downloadId))
                .Returns(includeTrackedDownload ? trackedDownload : null);

            Mocker.SetConstant(new IImportDecisionEngineSpecification[]
            {
                Mocker.Resolve<MatchesGrabSpecification>()
            }.AsEnumerable());

            var parsedRelease = Parser.Parser.ParseTitle(releaseTitle);

            return Subject.GetImportDecisions(videoFiles, _series, downloadClientItem, parsedRelease, parsedRelease, false, true);
        }

        [Test]
        public void should_call_all_specifications()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_videoFiles, _series, downloadClientItem, null, null, false, true);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEpisode>(), downloadClientItem), Times.Once());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_videoFiles, _series);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _series);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_approved_if_all_specs_pass()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _series);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_videoFiles, _series);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_parse()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _series);

            Mocker.GetMock<IAggregationService>()
                  .Verify(c => c.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Exactly(_videoFiles.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        public void should_not_throw_if_episodes_are_not_found()
        {
            GivenSpecifications(_pass1);

            _videoFiles = new List<string>
                {
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV",
                    "The.Office.S03E115.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            var decisions = Subject.GetImportDecisions(_videoFiles, _series);

            Mocker.GetMock<IAggregationService>()
                  .Verify(c => c.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Exactly(_videoFiles.Count));

            decisions.Should().HaveCount(3);
            decisions.First().Rejections.Should().NotBeEmpty();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Office.S03E115.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _series).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_reconcile_legacy_multi_season_grab_membership_from_tracked_download()
        {
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes);

            decisions.Should().HaveCount(7);
            decisions.SelectMany(decision => decision.Rejections)
                .Should().NotContain(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
            decisions.Should().OnlyContain(decision => decision.Approved);
        }

        [Test]
        public void ambiguous_numbering_should_block_automatic_import_even_when_other_specs_accept()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            Mocker.GetMock<IAggregationService>()
                .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Callback<LocalEpisode, DownloadClientItem>((localEpisode, item) =>
                {
                    localEpisode.Episodes = _localEpisode.Episodes;
                    localEpisode.NumberingRejection = "Ambiguous scene/library numbering";
                });
            var decision = Subject.GetImportDecisions(_videoFiles, _series).Single();
            decision.Approved.Should().BeFalse();
            decision.Rejections.Should().ContainSingle().Which.Reason.Should().Be(ImportRejectionReason.AmbiguousNumbering);
            _pass1.Verify(s => s.IsSatisfiedBy(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void saved_manual_mapping_should_override_automatic_numbering_rejection()
        {
            GivenSpecifications(_pass1);
            Mocker.GetMock<IManualEpisodeMappingService>()
                .Setup(s => s.Find(It.IsAny<LocalEpisode>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new ManualEpisodeMapping { Episodes = _localEpisode.Episodes });
            Mocker.GetMock<IAggregationService>()
                .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                .Callback<LocalEpisode, DownloadClientItem>((localEpisode, item) =>
                {
                    localEpisode.NumberingRejection = "Ambiguous scene/library numbering";
                });

            var decision = Subject.GetImportDecisions(_videoFiles, _series).Single();

            decision.Approved.Should().BeTrue();
            decision.LocalEpisode.Episodes.Should().Equal(_localEpisode.Episodes);
            decision.LocalEpisode.NumberingRejection.Should().BeNull();
        }

        [Test]
        public void should_not_reconcile_legacy_membership_for_single_season_release()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_for_individual_episode_release()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01E01.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_for_mismatched_series()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes(2);
            var trackedSeries = Builder<Series>.CreateNew().With(series => series.Id = 2).Build();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes, trackedSeries);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_without_tracked_episodes()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, new List<Episode>());

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_without_tracked_remote_episode()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, new List<Episode>(), includeRemoteEpisode: false);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_without_tracked_download()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, new List<Episode>(), includeTrackedDownload: false);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_without_successful_tracked_parse()
        {
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(null, trackedEpisodes);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_when_history_is_not_a_strict_subset()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes, historicalEpisodeIds: new[] { 101, 701, 999 });

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_keep_rejecting_file_episode_absent_from_reconciled_membership()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes, overriddenMiddleEpisodeId: 999);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 4)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_for_non_full_multi_season_release()
        {
            var parsedRelease = new ParsedEpisodeInfo
            {
                SeasonNumber = 1,
                SeasonNumbers = [1, 2, 3, 4, 5, 6, 7],
                FullSeason = false,
                IsPartialSeason = true
            };
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_discrete_multi_season_values_as_a_continuous_range()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01.S04.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_without_download_id()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes, downloadId: string.Empty);

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }

        [Test]
        public void should_not_reconcile_legacy_membership_for_different_tracked_download_id()
        {
            var parsedRelease = Parser.Parser.ParseTitle("Example.Show.S01-S07.1080p.WEB-DL");
            var trackedEpisodes = GetMappedMultiSeasonEpisodes();

            var decisions = GetLegacyMultiSeasonDecisions(parsedRelease, trackedEpisodes, trackedDownloadId: "different-download");

            decisions.Single(decision => decision.LocalEpisode.FileEpisodeInfo.SeasonNumber == 2)
                .Rejections.Should().ContainSingle(rejection => rejection.Reason == ImportRejectionReason.EpisodeNotFoundInRelease);
        }
    }
}
