using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.QueueTests
{
    [TestFixture]
    public class QueueServiceFixture : CoreTest<QueueService>
    {
        private List<TrackedDownload> _trackedDownloads;
        private ImportProgressService _importProgressService;

        [SetUp]
        public void SetUp()
        {
            var downloadClientInfo = Builder<DownloadClientItemClientInfo>.CreateNew()
                .With(i => i.Id = 7)
                .Build();

            var downloadItem = Builder<NzbDrone.Core.Download.DownloadClientItem>.CreateNew()
                                        .With(v => v.RemainingTime = TimeSpan.FromSeconds(10))
                                        .With(v => v.DownloadId = "ABC123")
                                        .With(v => v.DownloadClientInfo = downloadClientInfo)
                                        .Build();

            var series = Builder<Series>.CreateNew()
                                        .Build();

            var episodes = Builder<Episode>.CreateListOfSize(3)
                                          .All()
                                          .With(e => e.SeriesId = series.Id)
                                          .Build();

            var remoteEpisode = Builder<RemoteEpisode>.CreateNew()
                                                   .With(r => r.Series = series)
                                                   .With(r => r.Episodes = new List<Episode>(episodes))
                                                   .With(r => r.ParsedEpisodeInfo = new ParsedEpisodeInfo())
                                                   .Build();

            _trackedDownloads = Builder<TrackedDownload>.CreateListOfSize(1)
                .All()
                .With(v => v.IsTrackable = true)
                .With(v => v.DownloadItem = downloadItem)
                .With(v => v.RemoteEpisode = remoteEpisode)
                .Build()
                .ToList();

            _trackedDownloads.Single().DownloadClient = 7;
            _importProgressService = new ImportProgressService(Mocker.GetMock<IEventAggregator>().Object);
            Mocker.SetConstant<IImportProgressService>(_importProgressService);
        }

        [Test]
        public void queue_items_should_have_id()
        {
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queue = Subject.GetQueue();

            queue.Should().HaveCount(1);

            queue.All(v => v.Id > 0).Should().BeTrue();
        }

        [Test]
        public void should_attach_only_matching_composite_key_progress()
        {
            var episodes = _trackedDownloads.Single().RemoteEpisode.Episodes;
            _importProgressService.BeginBatch(7, "ABC123", episodes.Select(e => e.Id));
            _importProgressService.BeginBatch(8, "ABC123", new[] { 999 });

            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            Subject.GetQueue().Single().ImportProgress.TotalEpisodes.Should().Be(episodes.Select(e => e.Id).Distinct().Count());
            Subject.GetQueue().Single().ImportProgress.TotalEpisodes.Should().NotBe(1);
        }

        [Test]
        public void should_leave_queue_item_without_progress_unchanged()
        {
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            var queueItem = Subject.GetQueue().Single();
            queueItem.ImportProgress.Should().BeNull();
            queueItem.DownloadId.Should().Be("ABC123");
        }

        [Test]
        public void should_refresh_queue_snapshot_when_import_progress_changes()
        {
            var episode = _trackedDownloads.Single().RemoteEpisode.Episodes.First();
            var item = _trackedDownloads.Single().DownloadItem;
            var decision = new ImportDecision(new LocalEpisode
            {
                Path = @"C:\Import\Example Show S01E01.mkv",
                Episodes = new List<Episode> { episode }
            });
            _importProgressService.BeginBatch(7, "ABC123", new[] { episode.Id });
            _importProgressService.RegisterDecisions(item, new[] { decision });
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));

            _importProgressService.MarkImported(item, decision.LocalEpisode);
            Subject.Handle(new ImportProgressUpdatedEvent(new ImportProgressKey(7, "ABC123")));

            Subject.GetQueue().Single().ImportProgress.ImportedEpisodes.Should().Be(1);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<QueueUpdatedEvent>()), Times.AtLeastOnce());
        }

        [Test]
        public void should_replace_queue_object_when_import_progress_changes()
        {
            Subject.Handle(new TrackedDownloadRefreshedEvent(_trackedDownloads));
            var before = Subject.GetQueue().Single();

            _importProgressService.BeginBatch(7, "ABC123", new[] { 1 });
            Subject.Handle(new ImportProgressUpdatedEvent(new ImportProgressKey(7, "ABC123")));

            var after = Subject.GetQueue().Single();
            ReferenceEquals(before, after).Should().BeFalse();
            before.ImportProgress.Should().BeNull();
            after.ImportProgress.TotalEpisodes.Should().Be(1);
        }

        [Test]
        public void should_prune_progress_when_parent_leaves_queue()
        {
            _importProgressService.BeginBatch(7, "ABC123", new[] { 1 });

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload>()));

            _importProgressService.GetProgress(7, "ABC123").Should().BeNull();
        }
    }
}
