using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class CompleteSeriesSearchServiceFixture : CoreTest<CompleteSeriesSearchService>
    {
        private Series _series;
        private List<DownloadDecision> _decisions;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 42, Title = "Example", Monitored = true };
            _decisions = new List<DownloadDecision>
            {
                new DownloadDecision(new RemoteEpisode { Release = new ReleaseInfo { Title = "Example Complete" } }),
                new DownloadDecision(new RemoteEpisode { Release = new ReleaseInfo { Title = "Example Complete SD" } }, new DownloadRejection(DownloadRejectionReason.DiskNotUpgrade, "Not an upgrade"))
            };
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(_series.Id)).Returns(_series);
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.CompleteSeriesSearch(_series.Id, It.IsAny<bool>(), false)).ReturnsAsync(_decisions);
            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(_decisions))
                .ReturnsAsync(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), _decisions));
        }

        [TestCase(CommandTrigger.Manual, true)]
        [TestCase(CommandTrigger.Scheduled, false)]
        public void should_use_automatic_search_and_normal_download_selection(CommandTrigger trigger, bool userInvoked)
        {
            Subject.Execute(new CompleteSeriesSearchCommand { SeriesId = _series.Id, Trigger = trigger });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.CompleteSeriesSearch(_series.Id, userInvoked, false), Times.Once());
            Mocker.GetMock<IProcessDownloadDecisions>().Verify(s => s.ProcessDecisions(_decisions), Times.Once());
            Mocker.GetMock<ISearchForReleases>().VerifyNoOtherCalls();
        }

        [Test]
        public void dry_run_should_not_submit_downloads_or_pending_releases()
        {
            Subject.Execute(new CompleteSeriesSearchCommand { SeriesId = _series.Id, DryRun = true });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.CompleteSeriesSearch(_series.Id, false, false), Times.Once());
            Mocker.GetMock<IProcessDownloadDecisions>().VerifyNoOtherCalls();
        }

        [Test]
        public void unmonitored_series_should_not_search_or_download()
        {
            _series.Monitored = false;

            Subject.Execute(new CompleteSeriesSearchCommand { SeriesId = _series.Id });

            Mocker.GetMock<ISearchForReleases>().VerifyNoOtherCalls();
            Mocker.GetMock<IProcessDownloadDecisions>().VerifyNoOtherCalls();
        }

        [Test]
        public void no_complete_results_should_not_fall_back_to_seasons()
        {
            _decisions.Clear();

            Subject.Execute(new CompleteSeriesSearchCommand { SeriesId = _series.Id });

            Mocker.GetMock<ISearchForReleases>().Verify(s => s.CompleteSeriesSearch(_series.Id, false, false), Times.Once());
            Mocker.GetMock<ISearchForReleases>().VerifyNoOtherCalls();
        }
    }
}
