using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    [TestFixture]
    public class ImportProgressServiceFixture
    {
        private Mock<IEventAggregator> _eventAggregator;
        private ImportProgressService _subject;
        private DownloadClientItem _downloadItem;

        [SetUp]
        public void SetUp()
        {
            _eventAggregator = new Mock<IEventAggregator>();
            _subject = new ImportProgressService(_eventAggregator.Object);
            _downloadItem = new DownloadClientItem
            {
                DownloadId = "ABC123",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 7 }
            };
        }

        [Test]
        public void should_track_single_episode_lifecycle()
        {
            var decision = Decision("Example.Show.S01E01.mkv", Episode(101, 1, 1));

            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            _subject.RegisterDecisions(_downloadItem, new[] { decision });
            _subject.GetProgress(7, "ABC123").WaitingEpisodes.Should().Be(1);

            _subject.MarkImporting(_downloadItem, decision.LocalEpisode);
            _subject.GetProgress(7, "ABC123").ImportingEpisodes.Should().Be(1);

            _subject.MarkImported(_downloadItem, decision.LocalEpisode);
            var progress = _subject.GetProgress(7, "ABC123");
            progress.ImportedEpisodes.Should().Be(1);
            progress.Items.Should().ContainSingle(i => i.State == ImportProgressState.Imported && i.Started.HasValue && i.Completed.HasValue);
        }

        [Test]
        public void should_use_complete_unique_expected_episode_set_for_multi_season_batch()
        {
            var decisions = Enumerable.Range(1, 4)
                .Select(season => Decision($"Example.Show.S{season:00}E01.mkv", Episode((season * 100) + 1, season, 1)))
                .ToList();

            _subject.BeginBatch(7, "ABC123", new[] { 101, 201, 301, 401, 401 });
            _subject.RegisterDecisions(_downloadItem, decisions);

            var progress = _subject.GetProgress(7, "ABC123");
            progress.TotalEpisodes.Should().Be(4);
            progress.WaitingEpisodes.Should().Be(4);
            progress.Items.Select(i => i.SeasonNumber).Should().Equal(1, 2, 3, 4);
        }

        [Test]
        public void should_count_multi_episode_file_once_and_each_episode_once()
        {
            var decision = Decision("Example.Show.S01E01-E02.mkv", Episode(101, 1, 1), Episode(102, 1, 2));

            _subject.BeginBatch(7, "ABC123", new[] { 101, 102 });
            _subject.RegisterDecisions(_downloadItem, new[] { decision });
            _subject.MarkImported(_downloadItem, decision.LocalEpisode);

            var progress = _subject.GetProgress(7, "ABC123");
            progress.Items.Should().ContainSingle();
            progress.Items.Single().EpisodeIds.Should().Equal(101, 102);
            progress.ImportedEpisodes.Should().Be(2);
        }

        [Test]
        public void should_record_rejection_reason()
        {
            var decision = new ImportDecision(
                Local("Example.Show.S01E01.mkv", Episode(101, 1, 1)),
                new ImportRejection(ImportRejectionReason.Unknown, "Rejected exactly"));

            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            _subject.RegisterDecisions(_downloadItem, new[] { decision });

            var item = _subject.GetProgress(7, "ABC123").Items.Single();
            item.State.Should().Be(ImportProgressState.Rejected);
            item.Errors.Should().Equal("Rejected exactly");
        }

        [Test]
        public void rejected_alternative_should_not_override_successful_episode_coverage()
        {
            var rejected = new ImportDecision(
                Local("Example.Show.S01E01.bad.mkv", Episode(101, 1, 1)),
                new ImportRejection(ImportRejectionReason.Unknown, "Rejected"));
            var approved = Decision("Example.Show.S01E01.good.mkv", Episode(101, 1, 1));

            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            _subject.RegisterDecisions(_downloadItem, new[] { rejected, approved });
            _subject.MarkImported(_downloadItem, approved.LocalEpisode);

            var progress = _subject.GetProgress(7, "ABC123");
            progress.ImportedEpisodes.Should().Be(1);
            progress.RejectedEpisodes.Should().Be(0);
        }

        [Test]
        public void should_isolate_download_clients_with_the_same_download_id()
        {
            var otherItem = new DownloadClientItem
            {
                DownloadId = "ABC123",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 8 }
            };
            var decision = Decision("Example.Show.S01E01.mkv", Episode(101, 1, 1));

            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            _subject.BeginBatch(8, "ABC123", new[] { 101 });
            _subject.RegisterDecisions(_downloadItem, new[] { decision });
            _subject.MarkImported(_downloadItem, decision.LocalEpisode);

            _subject.GetProgress(7, "ABC123").ImportedEpisodes.Should().Be(1);
            _subject.GetProgress(8, "ABC123").ImportedEpisodes.Should().Be(0);
            _subject.GetProgress(8, "ABC123").Items.Should().BeEmpty();
            otherItem.DownloadClientInfo.Id.Should().Be(8);
        }

        [Test]
        public void should_prune_only_batches_not_in_the_active_key_set()
        {
            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            _subject.BeginBatch(8, "DEF456", new[] { 201 });

            _subject.Prune(new[] { new ImportProgressKey(7, "ABC123") });

            _subject.GetProgress(7, "ABC123").Should().NotBeNull();
            _subject.GetProgress(8, "DEF456").Should().BeNull();
        }

        [Test]
        public void should_not_expose_tracker_owned_collections()
        {
            _subject.BeginBatch(7, "ABC123", new[] { 101 });
            var decision = Decision("Example.Show.S01E01.mkv", Episode(101, 1, 1));
            _subject.RegisterDecisions(_downloadItem, new[] { decision });

            var snapshot = _subject.GetProgress(7, "ABC123");
            snapshot.Items.Clear();
            snapshot.Items.Should().BeEmpty();
            _subject.GetProgress(7, "ABC123").Items.Should().ContainSingle();
        }

        [Test]
        public void should_preserve_same_download_retry_when_expected_membership_is_unchanged()
        {
            _subject.BeginBatch(7, "ABC123", new[] { 101, 201 });
            var decision = Decision("Example.Show.S01E01.mkv", Episode(101, 1, 1));
            _subject.RegisterDecisions(_downloadItem, new[] { decision });
            _subject.MarkImported(_downloadItem, decision.LocalEpisode);
            _subject.BeginBatch(7, "ABC123", new[] { 101, 201 });

            var progress = _subject.GetProgress(7, "ABC123");
            progress.TotalEpisodes.Should().Be(2);
            progress.ImportedEpisodes.Should().Be(1);
        }

        [Test]
        public void should_start_fresh_when_expected_membership_changes()
        {
            _subject.BeginBatch(7, "ABC123", new[] { 101, 201 });
            var decision = Decision("Example.Show.S01E01.mkv", Episode(101, 1, 1));
            _subject.RegisterDecisions(_downloadItem, new[] { decision });
            _subject.MarkImported(_downloadItem, decision.LocalEpisode);
            _subject.BeginBatch(7, "ABC123", new[] { 101, 201, 301 });

            var progress = _subject.GetProgress(7, "ABC123");
            progress.TotalEpisodes.Should().Be(3);
            progress.ImportedEpisodes.Should().Be(0);
            progress.Items.Should().BeEmpty();
        }

        [Test]
        public void should_isolate_concurrent_updates_reads_and_pruning()
        {
            var decisions = Enumerable.Range(1, 20)
                .Select(number => Decision($"Example.Show.S01E{number:00}.mkv", Episode(number, 1, number)))
                .ToList();

            _subject.BeginBatch(7, "ABC123", decisions.SelectMany(d => d.LocalEpisode.Episodes).Select(e => e.Id));
            _subject.BeginBatch(8, "ABC123", new[] { 999 });
            _subject.RegisterDecisions(_downloadItem, decisions);

            var otherItem = new DownloadClientItem
            {
                DownloadId = "ABC123",
                DownloadClientInfo = new DownloadClientItemClientInfo { Id = 8 }
            };

            Parallel.For(0, 200, index =>
            {
                var decision = decisions[index % decisions.Count];
                if (index % 3 == 0)
                {
                    _subject.MarkImporting(_downloadItem, decision.LocalEpisode);
                }
                else
                {
                    _subject.MarkImported(_downloadItem, decision.LocalEpisode);
                }

                _subject.GetProgress(7, "ABC123").TotalEpisodes.Should().Be(20);
                _subject.GetProgress(8, "ABC123").TotalEpisodes.Should().Be(1);
                _subject.Prune(new[] { new ImportProgressKey(7, "ABC123"), new ImportProgressKey(8, "ABC123") });
                _subject.GetProgress(8, "ABC123").Should().NotBeNull();
                otherItem.DownloadClientInfo.Id.Should().Be(8);
            });
        }

        [Test]
        public void concurrent_registrations_should_produce_consistent_snapshot()
        {
            var decisions = Enumerable.Range(1, 100)
                .Select(number => Decision($"Example.Show.S01E{number:00}.mkv", Episode(number, 1, number)))
                .ToList();

            _subject.BeginBatch(7, "ABC123", Enumerable.Range(1, 100));
            Parallel.ForEach(decisions, decision => _subject.RegisterDecisions(_downloadItem, new[] { decision }));

            var progress = _subject.GetProgress(7, "ABC123");
            progress.TotalEpisodes.Should().Be(100);
            progress.WaitingEpisodes.Should().Be(100);
            progress.Items.Should().HaveCount(100);
            progress.Items.SelectMany(i => i.EpisodeIds).Distinct().Should().HaveCount(100);
        }

        private static ImportDecision Decision(string path, params Episode[] episodes)
        {
            return new ImportDecision(Local(path, episodes));
        }

        private static LocalEpisode Local(string path, params Episode[] episodes)
        {
            return new LocalEpisode
            {
                Path = Path.Combine(@"C:\Import".AsOsAgnostic(), path),
                Size = 1000,
                Episodes = episodes.ToList()
            };
        }

        private static Episode Episode(int id, int season, int number)
        {
            return new Episode { Id = id, SeasonNumber = season, EpisodeNumber = number };
        }
    }
}
