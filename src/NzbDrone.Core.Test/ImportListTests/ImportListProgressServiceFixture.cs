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
using NzbDrone.Core.ImportLists.Simkl.User;
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
        private readonly Dictionary<int, ImportListDefinition> _currentLists = new();
        private readonly List<ImportListItemInfo> _memberships = new();
        private readonly List<Episode> _episodes = new();
        private Mock<IImportListProgressWriter> _writer;
        private DateTime _now;
        private string _fingerprint;
        private Series _currentSeries;

        [SetUp]
        public void Setup()
        {
            _stored.Clear();
            _lists.Clear();
            _currentLists.Clear();
            _memberships.Clear();
            _episodes.Clear();
            _fingerprint = "generated-credential-hash";
            _now = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
            _lists.Add(new ImportListDefinition { Id = 8, Settings = new AniListSettings { SyncWatchedProgress = true }, EnableAutomaticAdd = true });
            _currentLists.Add(8, CopyDefinition(_lists[0]));
            _memberships.Add(new ImportListItemInfo { ImportListId = 8, AniListId = 12 });
            _episodes.AddRange(Enumerable.Range(1, 3).Select(number => new Episode { Id = number, SeasonNumber = 1, EpisodeNumber = number, ForeignId = "12:" + number, WatchedSource = "fixture-watch-source-a" }));
            _currentSeries = new Series { Id = 10, MetadataSource = MetadataSourceType.AniList, ForeignId = "12" };
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetSeries(10)).Returns(() => _currentSeries);
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodeBySeries(10)).Returns(_episodes);
            Mocker.GetMock<IImportListFactory>().Setup(service => service.All()).Returns(_lists);
            Mocker.GetMock<IImportListFactory>().Setup(service => service.Find(It.IsAny<int>())).Returns((int id) => _currentLists.GetValueOrDefault(id));
            Mocker.GetMock<IImportListItemService>().Setup(service => service.GetAllForLists(It.IsAny<List<int>>()))
                .Returns((List<int> ids) => _memberships.Where(item => ids.Contains(item.ImportListId)).ToList());
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
                    var context = definition.Settings switch
                    {
                        AniListSettings ani when ani.SyncWatchedProgress => "AniList:" + ani.Username + ":" + ani.AccessToken,
                        SimklUserSettings simkl when simkl.SyncWatchedProgress => "Simkl:" + simkl.AuthUser + ":" + simkl.AccessToken + ":" + simkl.ScopeV2,
                        _ => null
                    };
                    result = new ImportListProgressTarget { MediaId = 12, Fingerprint = _fingerprint + ":" + context };
                    return context != null && item.AniListId == 12 && series.ForeignId == "12";
                }));
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()))
                .Returns((ImportListDefinition definition, ImportListProgressTarget result, int progress) => progress);
            Mocker.SetConstant<IEnumerable<IImportListProgressWriter>>(new[] { _writer.Object });
        }

        private ImportListProgressState State => _stored[(8, 10)];

        private static ImportListDefinition CopyDefinition(ImportListDefinition definition)
        {
            var settings = (AniListSettings)definition.Settings;
            return new ImportListDefinition
            {
                Id = definition.Id,
                Implementation = definition.Implementation,
                EnableAutomaticAdd = definition.EnableAutomaticAdd,
                Settings = new AniListSettings
                {
                    SyncWatchedProgress = settings.SyncWatchedProgress,
                    Username = settings.Username,
                    AccessToken = settings.AccessToken
                }
            };
        }

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
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            State.AcknowledgedProgress.Should().Be(2);
            State.FailureCount.Should().Be(0);
            State.AcknowledgedIdentityFingerprint.Should().HaveLength(64);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Once());
        }

        [Test]
        public void should_not_advance_a_gap_or_use_an_unrelated_list_membership()
        {
            Subject.SyncProgress(10, new[] { 2, 3 }, () => true);
            _stored.Should().BeEmpty();
            _memberships.Single().AniListId = 13;
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _stored.Should().BeEmpty();
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_write_back_a_disabled_import_definition()
        {
            _lists.Single().EnableAutomaticAdd = false;
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _stored.Should().BeEmpty();
        }

        [Test]
        public void should_resume_persisted_retries_after_restart_without_lowering_pending_progress()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2)).Throws(new InvalidOperationException("PRIVATE-ERROR-BODY"));
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            State.PendingProgress.Should().Be(2);
            State.AcknowledgedProgress.Should().Be(0);
            State.NextAttemptUtc.Should().Be(_now.AddMinutes(5));
            Subject.SyncProgress(10, new[] { 1 }, () => true);
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
            restarted.SyncProgress(10, new[] { 1 }, () => true);
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
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            State.NextAttemptUtc.Should().Be(_now.AddMinutes(15));
            State.FailureCount.Should().Be(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_wait_for_a_changed_account_or_reauthentication_after_an_authority_failure()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>())).Throws(new ImportListProgressUnavailableException());
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            State.NextAttemptUtc.Should().Be(_now.AddDays(1));
            _fingerprint = "different-credential-hash";
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>())).Returns(1);
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            State.AcknowledgedProgress.Should().Be(1);
            State.FailureCount.Should().Be(0);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_acknowledgements_when_future_episodes_are_added_but_reset_changed_native_ids()
        {
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _episodes.Add(new Episode { Id = 4, SeasonNumber = 1, EpisodeNumber = 4, ForeignId = "12:4" });
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Once());
            _episodes.First().ForeignId = "changed-native-identity";
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2), Times.Exactly(2));
        }

        [Test]
        public void should_persist_partial_history_chunks_and_resume_without_an_error()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2)).Returns(1);
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
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
                _currentLists.Add(id, CopyDefinition(_lists.Last()));
                _memberships.Add(new ImportListItemInfo { ImportListId = id, AniListId = 12 });
            }

            Subject.SyncProgress(10, new[] { 1 }, () => true);
            _stored.Should().HaveCount(11);
            _stored.Values.Count(state => state.AcknowledgedProgress == 1).Should().Be(10);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 1), Times.Exactly(10));
            _now = _now.AddMinutes(1);
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            _stored.Values.Should().OnlyContain(state => state.AcknowledgedProgress == 1);
        }

        [Test]
        public void should_remove_pending_rows_when_a_list_or_series_is_deleted()
        {
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            Subject.HandleAsync(new ProviderDeletedEvent<IImportList>(8));
            Subject.HandleAsync(new SeriesDeletedEvent(new List<Series> { new Series { Id = 10 } }, false, false));
            Mocker.GetMock<IImportListProgressStateRepository>().Verify(repository => repository.DeleteMany(It.Is<List<ImportListProgressState>>(states => states.Count == 1 && states[0].SeriesId == 10)), Times.Exactly(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_fail_closed_for_false_or_missing_source_authorization_and_keep_pending_work(bool missing)
        {
            Subject.SyncProgress(10, new[] { 1, 2 }, missing ? null : () => false);
            State.PendingProgress.Should().Be(2);
            State.AcknowledgedProgress.Should().Be(0);
            State.LastAttemptUtc.Should().BeNull();
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_stop_later_list_targets_when_global_authorization_is_revoked_during_first_advance()
        {
            _lists.Add(new ImportListDefinition { Id = 9, Settings = new AniListSettings { SyncWatchedProgress = true }, EnableAutomaticAdd = true });
            _currentLists.Add(9, CopyDefinition(_lists.Last()));
            _memberships.Add(new ImportListItemInfo { ImportListId = 9, AniListId = 12 });
            var allowed = true;
            _writer.Setup(writer => writer.Advance(It.Is<ImportListDefinition>(definition => definition.Id == 8), It.IsAny<ImportListProgressTarget>(), 1))
                .Returns(() =>
                {
                    allowed = false;
                    return 1;
                });
            Subject.SyncProgress(10, new[] { 1 }, () => allowed);
            State.AcknowledgedProgress.Should().Be(1);
            _stored[(9, 10)].PendingProgress.Should().Be(1);
            _stored[(9, 10)].AcknowledgedProgress.Should().Be(0);
            _stored[(9, 10)].LastAttemptUtc.Should().BeNull();
            _writer.Verify(writer => writer.Advance(It.Is<ImportListDefinition>(definition => definition.Id == 9), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase("deleted")]
        [TestCase("disabled")]
        [TestCase("opt_out")]
        [TestCase("account")]
        [TestCase("token")]
        [TestCase("membership")]
        [TestCase("native_series")]
        [TestCase("scope")]
        public void should_recheck_current_list_account_scope_and_native_provenance_between_targets(string change)
        {
            var second = new ImportListDefinition { Id = 9, Settings = new AniListSettings { SyncWatchedProgress = true }, EnableAutomaticAdd = true };
            _lists.Add(second);
            _currentLists.Add(9, CopyDefinition(second));
            _memberships.Add(new ImportListItemInfo { ImportListId = 9, AniListId = 12 });
            if (change == "scope")
            {
                second.Settings = new SimklUserSettings { SyncWatchedProgress = true, ScopeV2 = "media:read media:write" };
                _currentLists[9].Settings = new SimklUserSettings { SyncWatchedProgress = true, ScopeV2 = "media:read media:write" };
            }

            _writer.Setup(writer => writer.Advance(It.Is<ImportListDefinition>(definition => definition.Id == 8), It.IsAny<ImportListProgressTarget>(), 1))
                .Returns(() =>
                {
                    switch (change)
                    {
                        case "deleted":
                            _currentLists.Remove(9);
                            break;
                        case "disabled":
                            _currentLists[9].EnableAutomaticAdd = false;
                            break;
                        case "opt_out":
                            ((AniListSettings)_currentLists[9].Settings).SyncWatchedProgress = false;
                            break;
                        case "account":
                            ((AniListSettings)_currentLists[9].Settings).Username = "different-owner";
                            break;
                        case "token":
                            ((AniListSettings)_currentLists[9].Settings).AccessToken = "different-token";
                            break;
                        case "membership":
                            _memberships.RemoveAll(item => item.ImportListId == 9);
                            break;
                        case "native_series":
                            _currentSeries = new Series { Id = 10, MetadataSource = MetadataSourceType.AniList, ForeignId = "13" };
                            break;
                        case "scope":
                            ((SimklUserSettings)_currentLists[9].Settings).ScopeV2 = "media:read";
                            break;
                    }

                    return 1;
                });
            Subject.SyncProgress(10, new[] { 1 }, () => true);
            second.EnableAutomaticAdd.Should().BeTrue();
            _stored[(9, 10)].PendingProgress.Should().Be(1);
            _stored[(9, 10)].AcknowledgedProgress.Should().Be(0);
            _stored[(9, 10)].LastAttemptUtc.Should().BeNull();
            _writer.Verify(writer => writer.Advance(It.Is<ImportListDefinition>(definition => definition.Id == 9), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_drop_another_watch_sources_pending_prefix_after_a_user_or_library_change()
        {
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 2))
                .Throws(new InvalidOperationException("generated provider outage"));
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            State.PendingProgress.Should().Be(2);
            State.AcknowledgedProgress.Should().Be(0);
            var priorFingerprint = State.TargetFingerprint;
            foreach (var episode in _episodes)
            {
                episode.WatchedSource = "fixture-watch-source-b";
            }

            Subject.SyncProgress(10, new[] { 1 }, () => true);
            State.TargetFingerprint.Should().NotBe(priorFingerprint);
            State.PendingProgress.Should().Be(1);
            State.AcknowledgedProgress.Should().Be(1);
            State.FailureCount.Should().Be(0);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), 1), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_recheck_remote_progress_after_a_watch_source_change_without_lowering_existing_remote_progress()
        {
            var remoteProgress = 2;
            _writer.Setup(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()))
                .Returns((ImportListDefinition definition, ImportListProgressTarget target, int progress) =>
                {
                    remoteProgress = Math.Max(remoteProgress, progress);
                    return remoteProgress;
                });
            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            foreach (var episode in _episodes)
            {
                episode.WatchedSource = "fixture-watch-source-b";
            }

            Subject.SyncProgress(10, new[] { 1 }, () => true);
            State.PendingProgress.Should().Be(1);
            State.AcknowledgedProgress.Should().Be(2);
            remoteProgress.Should().Be(2);
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("mixed")]
        public void should_refuse_missing_or_mixed_native_watch_sources(string source)
        {
            foreach (var episode in _episodes)
            {
                episode.WatchedSource = source == "mixed" ? "fixture-watch-source-a" : source;
            }

            if (source == "mixed")
            {
                _episodes[1].WatchedSource = "fixture-watch-source-b";
            }

            Subject.SyncProgress(10, new[] { 1, 2 }, () => true);
            _stored.Should().BeEmpty();
            _writer.Verify(writer => writer.Advance(It.IsAny<ImportListDefinition>(), It.IsAny<ImportListProgressTarget>(), It.IsAny<int>()), Times.Never());
        }
    }
}
