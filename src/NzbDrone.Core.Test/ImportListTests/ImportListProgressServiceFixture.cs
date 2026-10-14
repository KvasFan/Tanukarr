using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListProgressServiceFixture : CoreTest<ImportListProgressService>
    {
        private delegate bool TargetCallback(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget result);

        private readonly Dictionary<(int List, int Series), ImportListProgressState> _stored = new();
        private readonly List<ImportListDefinition> _lists = new();
        private readonly List<ImportListItemInfo> _memberships = new();
        private readonly List<Episode> _episodes = new();
        private Mock<IImportListProgressWriter> _writer;
        private DateTime _now;
        private string _fingerprint;

        [SetUp]
        public void Setup()
        {
            _stored.Clear();
            _lists.Clear();
            _memberships.Clear();
            _episodes.Clear();
            _fingerprint = "generated-credential-hash";
            _now = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
            _lists.Add(new ImportListDefinition { Id = 8, Settings = new AniListSettings { SyncWatchedProgress = true }, EnableAutomaticAdd = true });
            _memberships.Add(new ImportListItemInfo { ImportListId = 8, AniListId = 12 });
            _episodes.AddRange(Enumerable.Range(1, 3).Select(number => new Episode { Id = number, SeasonNumber = 1, EpisodeNumber = number, ForeignId = "12:" + number }));
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(10)).Returns(new Series { Id = 10, MetadataSource = MetadataSourceType.AniList, ForeignId = "12" });
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodeBySeries(10)).Returns(_episodes);
            Mocker.GetMock<IImportListFactory>().Setup(service => service.All()).Returns(_lists);
            Mocker.GetMock<IImportListItemService>().Setup(service => service.GetAllForLists(It.IsAny<List<int>>())).Returns(_memberships);
            Mocker.GetMock<IImportListProgressClock>().SetupGet(clock => clock.UtcNow).Returns(() => _now);
            Mocker.GetMock<IImportListProgressStateRepository>().Setup(repository => repository.ForListSeries(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int list, int series) => _stored.GetValueOrDefault((list, series)));
            Mocker.GetMock<IImportListProgressStateRepository>().Setup(repository => repository.Insert(It.IsAny<ImportListProgressState>()))
                .Returns((ImportListProgressState state) =>
                {
                    state.Id = _stored.Count + 1;
                    _stored[(state.ImportListId, state.SeriesId)] = state;
                    return state;
                });
            Mocker.GetMock<IImportListProgressStateRepository>().Setup(repository => repository.Update(It.IsAny<ImportListProgressState>()))
                .Returns((ImportListProgressState state) =>
                {
                    _stored[(state.ImportListId, state.SeriesId)] = state;
                    return state;
                });
            Mocker.GetMock<IImportListProgressStateRepository>().Setup(repository => repository.All()).Returns(() => _stored.Values.ToList());
            _writer = new Mock<IImportListProgressWriter>();
            var target = new ImportListProgressTarget();
            _writer.Setup(writer => writer.TryGetTarget(It.IsAny<ImportListDefinition>(), It.IsAny<Series>(), It.IsAny<ImportListItemInfo>(), out target))
                .Returns(new TargetCallback((ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget result) =>
                {
                    result = new ImportListProgressTarget { MediaId = 12, Fingerprint = _fingerprint };
                    return item.AniListId == 12;
                }));
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()))
                .Returns((ImportListDefinition definition, ImportListProgressTarget result, int progress) => progress);
            Mocker.SetConstant<IEnumerable<IImportListProgressWriter>>(new[] { _writer.Object });
        }

        private ImportListProgressState State => _stored[(8, 10)];

        [Test]
        public void should_persist_before_network_then_acknowledge_once_without_duplicate_writes()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2))
                .Returns(() =>
                {
                    State.PendingProgress.Should().Be(2);
                    State.LastAttemptUtc.Should().Be(_now);
                    State.NextAttemptUtc.Should().Be(_now.AddMinutes(5));
                    return 2;
                });
            Subject.SyncProgress(10, new[] { 1, 2 });
            Subject.SyncProgress(10, new[] { 1, 2 });
            State.AcknowledgedProgress.Should().Be(2);
            State.FailureCount.Should().Be(0);
            State.AcknowledgedIdentityFingerprint.Should().HaveLength(64);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Once());
        }

        [Test]
        public void should_not_advance_a_gap_or_use_an_unrelated_list_membership()
        {
            Subject.SyncProgress(10, new[] { 2, 3 });
            _stored.Should().BeEmpty();
            _memberships.Single().AniListId = 13;
            Subject.SyncProgress(10, new[] { 1, 2 });
            _stored.Should().BeEmpty();
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_write_back_a_disabled_import_definition()
        {
            _lists.Single().EnableAutomaticAdd = false;
            Subject.SyncProgress(10, new[] { 1, 2 });
            _stored.Should().BeEmpty();
        }

        [Test]
        public void should_resume_persisted_retries_after_restart_without_lowering_pending_progress()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2)).Throws(new InvalidOperationException("PRIVATE-ERROR-BODY"));
            Subject.SyncProgress(10, new[] { 1, 2 });
            State.PendingProgress.Should().Be(2);
            State.AcknowledgedProgress.Should().Be(0);
            State.NextAttemptUtc.Should().Be(_now.AddMinutes(5));
            Subject.SyncProgress(10, new[] { 1 });
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Once());
            _now = _now.AddMinutes(6);
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2)).Returns(2);
            var restarted = new ImportListProgressService(Mocker.GetMock<ISeriesService>().Object,
                Mocker.GetMock<IEpisodeService>().Object,
                Mocker.GetMock<IImportListFactory>().Object,
                Mocker.GetMock<IImportListItemService>().Object,
                Mocker.GetMock<IImportListProgressStateRepository>().Object,
                new[] { _writer.Object },
                Mocker.GetMock<IImportListProgressClock>().Object,
                TestLogger);
            restarted.SyncProgress(10, new[] { 1 });
            State.AcknowledgedProgress.Should().Be(2);
            State.FailureCount.Should().Be(0);
            _stored.Should().HaveCount(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_honor_provider_retry_after_and_persist_backoff()
        {
            var request = new HttpRequest("https://graphql.anilist.co");
            var limited = new TooManyRequestsException(request, new HttpResponse(request, new HttpHeader { { "Retry-After", "900" } }, "{}", HttpStatusCode.TooManyRequests));
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>())).Throws(limited);
            Subject.SyncProgress(10, new[] { 1 });
            State.NextAttemptUtc.Should().Be(_now.AddMinutes(15));
            State.FailureCount.Should().Be(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_wait_for_a_changed_account_or_reauthentication_after_an_authority_failure()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>())).Throws(new ImportListProgressUnavailableException());
            Subject.SyncProgress(10, new[] { 1 });
            State.NextAttemptUtc.Should().Be(_now.AddDays(1));
            _fingerprint = "different-credential-hash";
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>())).Returns(1);
            Subject.SyncProgress(10, new[] { 1 });
            State.AcknowledgedProgress.Should().Be(1);
            State.FailureCount.Should().Be(0);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_acknowledgements_when_future_episodes_are_added_but_reset_changed_native_ids()
        {
            Subject.SyncProgress(10, new[] { 1, 2 });
            _episodes.Add(new Episode { Id = 4, SeasonNumber = 1, EpisodeNumber = 4, ForeignId = "12:4" });
            Subject.SyncProgress(10, new[] { 1, 2 });
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Once());
            _episodes.First().ForeignId = "changed-native-identity";
            Subject.SyncProgress(10, new[] { 1, 2 });
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Exactly(2));
        }

        [Test]
        public void should_persist_partial_history_chunks_and_resume_without_an_error()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2)).Returns(1);
            Subject.SyncProgress(10, new[] { 1, 2 });
            State.PendingProgress.Should().Be(2);
            State.AcknowledgedProgress.Should().Be(1);
            State.NextAttemptUtc.Should().Be(_now.AddMinutes(1));
            State.FailureCount.Should().Be(0);
        }

        [Test]
        public void should_limit_a_large_existing_library_to_ten_targets_per_minute()
        {
            for (var id = 9; id <= 18; id++)
            {
                _lists.Add(new ImportListDefinition { Id = id, Settings = new AniListSettings { SyncWatchedProgress = true }, EnableAutomaticAdd = true });
                _memberships.Add(new ImportListItemInfo { ImportListId = id, AniListId = 12 });
            }

            Subject.SyncProgress(10, new[] { 1 });
            _stored.Should().HaveCount(11);
            _stored.Values.Count(state => state.AcknowledgedProgress == 1).Should().Be(10);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 1), Times.Exactly(10));
            _now = _now.AddMinutes(1);
            Subject.SyncProgress(10, new[] { 1 });
            _stored.Values.Should().OnlyContain(state => state.AcknowledgedProgress == 1);
        }

        [Test]
        public void should_remove_pending_rows_when_a_list_or_series_is_deleted()
        {
            Subject.SyncProgress(10, new[] { 1 });
            Subject.HandleAsync(new ProviderDeletedEvent<IImportList>(8));
            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { new Series { Id = 10 } }, false, false));
            Mocker.GetMock<IImportListProgressStateRepository>().Verify(repository => repository.DeleteMany(It.Is<List<ImportListProgressState>>(states => states.Count == 1 && states[0].SeriesId == 10)), Times.Exactly(2));
        }
    }
}
