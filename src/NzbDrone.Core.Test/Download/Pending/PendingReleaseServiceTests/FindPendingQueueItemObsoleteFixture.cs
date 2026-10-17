using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Crypto;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Download.Pending.PendingReleaseServiceTests
{
    [TestFixture]
    public class FindPendingQueueItemObsoleteFixture : CoreTest<PendingReleaseService>
    {
        private List<PendingRelease> _pending;
        private Series _series;
        private List<Episode> _episodes;

        [SetUp]
        public void Setup()
        {
            _pending = new List<PendingRelease>();

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.QualityProfile = new LazyLoaded<QualityProfile>(new QualityProfile
                                     {
                                         Name = "Test",
                                         Cutoff = Quality.HDTV720p.Id,
                                         Items = QualityFixture.GetDefaultQualities()
                                     }))
                                     .Build();

            _episodes = Builder<Episode>.CreateListOfSize(2)
                                        .All()
                                        .With(e => e.SeriesId = _series.Id)
                                        .Build()
                                        .ToList();

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(s => s.All())
                  .Returns(_pending);

            Mocker.GetMock<IPendingReleaseRepository>()
                  .Setup(s => s.AllBySeriesId(It.IsAny<int>()))
                  .Returns(_pending);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(It.IsAny<int>()))
                  .Returns(_series);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Series> { _series });

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<Series>()))
                  .Returns(new RemoteEpisode { Episodes = _episodes });

            var delayProfile = new DelayProfile { Order = 1, PreferredProtocol = DownloadProtocol.Usenet };

            Mocker.GetMock<IDelayProfileService>()
                  .Setup(s => s.AllForTags(It.IsAny<HashSet<int>>()))
                  .Returns(new List<DelayProfile> { delayProfile });

            Mocker.GetMock<IDelayProfileService>()
                  .Setup(s => s.BestForTags(It.IsAny<HashSet<int>>()))
                  .Returns(delayProfile);
        }

        private void GivenMultiEpisodePendingRelease(int id)
        {
            _pending.Add(new PendingRelease
            {
                Id = id,
                SeriesId = _series.Id,
                Title = "Series.Title.S01E01-E02.abc-Sonarr",
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeasonNumbers = [1],
                    EpisodeNumbers = [1, 2],
                    Quality = new QualityModel(Quality.HDTV720p)
                },
                Release = Builder<ReleaseInfo>.CreateNew().Build(),
                Reason = PendingReleaseReason.Delay
            });

            Subject.Handle(new ApplicationStartedEvent());
        }

        private static int ObsoleteQueueId(int pendingReleaseId, int episodeId)
        {
            return HashConverter.GetHashInt31(string.Format("pending-{0}-ep{1}", pendingReleaseId, episodeId));
        }

        private static int QueueId(int pendingReleaseId)
        {
            return HashConverter.GetHashInt31(string.Format("pending-{0}", pendingReleaseId));
        }

        [Test]
        public void should_find_item_by_each_v3_episode_queue_id()
        {
            GivenMultiEpisodePendingRelease(1);

            foreach (var episode in _episodes)
            {
                var item = Subject.FindPendingQueueItemObsolete(ObsoleteQueueId(1, episode.Id));

                item.Should().NotBeNull();
                item.Id.Should().Be(ObsoleteQueueId(1, episode.Id));
                item.Title.Should().Be(_pending.Single().Title);
            }
        }

        [Test]
        public void should_find_every_item_returned_by_the_v3_queue()
        {
            GivenMultiEpisodePendingRelease(1);

            var queue = Subject.GetPendingQueueObsolete();

            queue.Should().HaveCount(_episodes.Count);
            queue.Select(q => Subject.FindPendingQueueItemObsolete(q.Id)).Should().OnlyContain(q => q != null);
        }

        [Test]
        public void should_keep_v3_and_v5_queue_ids_separate()
        {
            GivenMultiEpisodePendingRelease(1);

            Subject.FindPendingQueueItem(QueueId(1)).Should().NotBeNull();
            Subject.FindPendingQueueItemObsolete(QueueId(1)).Should().BeNull();
            Subject.FindPendingQueueItem(ObsoleteQueueId(1, _episodes.First().Id)).Should().BeNull();
        }

        [Test]
        public void should_return_null_for_unknown_id()
        {
            GivenMultiEpisodePendingRelease(1);

            Subject.FindPendingQueueItemObsolete(ObsoleteQueueId(2, _episodes.First().Id)).Should().BeNull();
        }

        [Test]
        public void should_drop_v3_removed_release_from_both_queues_and_publish_update()
        {
            GivenMultiEpisodePendingRelease(1);
            GivenSingleEpisodePendingRelease(2);

            Subject.RemovePendingQueueItemsObsolete(ObsoleteQueueId(1, _episodes.First().Id));

            Mocker.GetMock<IPendingReleaseRepository>().Verify(c => c.DeleteMany(It.Is<IEnumerable<int>>(s => s.SequenceEqual(new[] { 1 }))), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(c => c.PublishEvent(It.IsAny<PendingReleasesUpdatedEvent>()), Times.Once());

            Subject.FindPendingQueueItemObsolete(ObsoleteQueueId(1, _episodes.First().Id)).Should().BeNull();
            Subject.FindPendingQueueItem(QueueId(1)).Should().BeNull();
            Subject.GetPendingQueueObsolete().Should().OnlyContain(q => q.Id != ObsoleteQueueId(1, _episodes.First().Id) && q.Id != ObsoleteQueueId(1, _episodes.Last().Id));
            Subject.GetPendingQueue().Select(q => q.Id).Should().BeEquivalentTo(new[] { QueueId(2) });
        }

        [Test]
        public void should_drop_v5_removed_release_from_both_queues_and_publish_update()
        {
            GivenMultiEpisodePendingRelease(1);
            GivenSingleEpisodePendingRelease(2);

            Subject.RemovePendingQueueItems(QueueId(2));

            Mocker.GetMock<IPendingReleaseRepository>().Verify(c => c.DeleteMany(It.Is<IEnumerable<int>>(s => s.SequenceEqual(new[] { 2 }))), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(c => c.PublishEvent(It.IsAny<PendingReleasesUpdatedEvent>()), Times.Once());

            Subject.FindPendingQueueItem(QueueId(2)).Should().BeNull();
            Subject.GetPendingQueue().Select(q => q.Id).Should().BeEquivalentTo(new[] { QueueId(1) });
            Subject.GetPendingQueueObsolete().Select(q => q.Id).Should().BeEquivalentTo(_episodes.Select(e => ObsoleteQueueId(1, e.Id)));
        }

        private void GivenSingleEpisodePendingRelease(int id)
        {
            _pending.Add(new PendingRelease
            {
                Id = id,
                SeriesId = _series.Id,
                Title = "Series.Title.S01E03.abc-Sonarr",
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    SeasonNumbers = [1],
                    EpisodeNumbers = [3],
                    Quality = new QualityModel(Quality.HDTV720p)
                },
                Release = Builder<ReleaseInfo>.CreateNew().Build(),
                Reason = PendingReleaseReason.Delay
            });

            Subject.Handle(new ApplicationStartedEvent());
        }
    }
}
