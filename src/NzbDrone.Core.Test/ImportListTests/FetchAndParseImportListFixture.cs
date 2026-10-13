using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class FetchAndParseImportListServiceFixture : CoreTest<FetchAndParseImportListService>
    {
        private List<IImportList> _importLists;
        private List<ImportListItemInfo> _listSeries;
        private ImportListStatus _aniListStatus;
        private List<ImportListItemInfo> _savedAniListItems;

        [SetUp]
        public void Setup()
        {
            _importLists = new List<IImportList>();

            Mocker.GetMock<IImportListFactory>()
                  .Setup(v => v.AutomaticAddEnabled(It.IsAny<bool>()))
                  .Returns(_importLists);

            _listSeries = Builder<ImportListItemInfo>.CreateListOfSize(5)
                .Build().ToList();

            Mocker.GetMock<ISearchForNewSeries>()
                .Setup(v => v.SearchForNewSeriesByImdbId(It.IsAny<string>(), It.IsAny<Language>()))
                .Returns((string value, Language _) => new List<Tv.Series>() { new Tv.Series() { ImdbId = value } });
        }

        private Mock<IImportList> WithList(int id, bool enabled, bool enabledAuto, ImportListFetchResult fetchResult, TimeSpan? minRefresh = null, int? lastSyncOffset = null, int? syncDeletedCount = null)
        {
            return CreateListResult(id, enabled, enabledAuto, fetchResult, minRefresh, lastSyncOffset, syncDeletedCount);
        }

        private Mock<IImportList> CreateListResult(int id, bool enabled, bool enabledAuto, ImportListFetchResult fetchResult, TimeSpan? minRefresh = null, int? lastSyncOffset = null, int? syncDeletedCount = null)
        {
            var refreshInterval = minRefresh ?? TimeSpan.FromHours(12);
            var importListDefinition = new ImportListDefinition { Id = id, Enable = enabled, EnableAutomaticAdd = enabledAuto, MinRefreshInterval = refreshInterval };

            var mockImportList = new Mock<IImportList>();
            mockImportList.SetupGet(s => s.Definition).Returns(importListDefinition);
            mockImportList.Setup(s => s.Fetch()).Returns(fetchResult);
            mockImportList.SetupGet(s => s.MinRefreshInterval).Returns(refreshInterval);

            DateTime? lastSync = lastSyncOffset.HasValue ? DateTime.UtcNow.AddHours(lastSyncOffset.Value) : null;
            Mocker.GetMock<IImportListStatusService>()
                .Setup(v => v.GetListStatus(id))
                .Returns(new ImportListStatus() { LastInfoSync = lastSync });

            if (syncDeletedCount.HasValue)
            {
                Mocker.GetMock<IImportListItemService>()
                    .Setup(v => v.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), id))
                    .Returns(syncDeletedCount.Value);
            }

            _importLists.Add(mockImportList.Object);

            return mockImportList;
        }

        private Mock<IImportList> WithAniList()
        {
            var items = new[] { new ImportListItemInfo { AniListId = 900101, Title = "Fixture series" } };
            var list = WithList(10, true, true, new ImportListFetchResult(items, false));
            var definition = (ImportListDefinition)list.Object.Definition;
            definition.Implementation = nameof(AniListImport);
            definition.Name = "Fixture AniList";
            definition.RootFolderPath = "/fixture-library";
            definition.Settings = new AniListSettings
            {
                Username = "fixture-user",
                AccessToken = "fixture-access-token",
                RefreshToken = "fixture-refresh-token",
                Expires = DateTime.UtcNow.AddDays(30)
            };

            _aniListStatus = new ImportListStatus { ProviderId = 10 };
            _savedAniListItems = new List<ImportListItemInfo>();
            Mocker.GetMock<IImportListStatusService>()
                  .Setup(service => service.GetListStatus(10))
                  .Returns(() => _aniListStatus);
            Mocker.GetMock<IImportListStatusService>()
                  .Setup(service => service.UpdateListSyncStatus(10, It.IsAny<bool>()))
                  .Callback<int, bool>((_, removed) =>
                  {
                      _aniListStatus.LastInfoSync = DateTime.UtcNow;
                      _aniListStatus.HasRemovedItemSinceLastClean |= removed;
                  });
            Mocker.GetMock<IImportListItemService>()
                  .Setup(service => service.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), 10))
                  .Returns((List<ImportListItemInfo> saved, int _) =>
                  {
                      var removed = _savedAniListItems.Count(item => saved.All(next => next.AniListId != item.AniListId));
                      _savedAniListItems = saved.ToList();
                      return removed;
                  });
            Mocker.GetMock<IImportListItemService>()
                  .Setup(service => service.GetAllForLists(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { 10 }))))
                  .Returns(() => _savedAniListItems);

            return list;
        }

        private FetchAndParseImportListService AnotherService(ICacheManager cacheManager)
        {
            return new FetchAndParseImportListService(
                Mocker.GetMock<IImportListFactory>().Object,
                Mocker.GetMock<IImportListStatusService>().Object,
                Mocker.GetMock<IImportListItemService>().Object,
                cacheManager,
                LogManager.GetLogger("Fixture import list fetcher"));
        }

        [Test]
        public void should_resume_saved_anilist_items_without_refetching_or_resyncing_a_recent_complete_list()
        {
            var list = WithAniList();
            Subject.Fetch().Series.Should().ContainSingle();
            var completedAt = _aniListStatus.LastInfoSync;

            var result = Subject.Fetch();

            result.Series.Select(item => item.AniListId).Should().Equal(900101);
            result.AnyFailure.Should().BeFalse();
            list.Verify(provider => provider.Fetch(), Times.Once());
            Mocker.GetMock<IImportListItemService>().Verify(service => service.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), 10), Times.Once());
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.UpdateListSyncStatus(10, It.IsAny<bool>()), Times.Once());
            _aniListStatus.LastInfoSync.Should().Be(completedAt);
        }

        [Test]
        public void should_share_successful_anilist_witness_between_service_instances_in_the_same_process()
        {
            var list = WithAniList();
            Subject.Fetch();

            AnotherService(Mocker.Resolve<ICacheManager>()).Fetch().Series.Should().ContainSingle();

            list.Verify(provider => provider.Fetch(), Times.Once());
        }

        [Test]
        public void should_not_resume_after_restart_without_a_process_local_complete_fetch_witness()
        {
            var list = WithAniList();
            Subject.Fetch();

            AnotherService(new CacheManager()).Fetch().Series.Should().BeEmpty();

            list.Verify(provider => provider.Fetch(), Times.Once());
        }

        [TestCase("username")]
        [TestCase("access-token")]
        [TestCase("refresh-token")]
        [TestCase("token-expiry")]
        [TestCase("included-list")]
        [TestCase("excluded-list")]
        [TestCase("included-status")]
        [TestCase("excluded-status")]
        [TestCase("media-status")]
        [TestCase("base-url")]
        [TestCase("root-folder")]
        [TestCase("tags")]
        public void should_refuse_saved_membership_after_definition_or_settings_change(string change)
        {
            var list = WithAniList();
            Subject.Fetch();
            var definition = (ImportListDefinition)list.Object.Definition;
            var settings = (AniListSettings)definition.Settings;

            switch (change)
            {
                case "username":
                    settings.Username = "different-fixture-user";
                    break;
                case "access-token":
                    settings.AccessToken = "changed-fixture-access-token";
                    break;
                case "refresh-token":
                    settings.RefreshToken = "changed-fixture-refresh-token";
                    break;
                case "token-expiry":
                    settings.Expires = settings.Expires.AddDays(1);
                    break;
                case "included-list":
                    settings.IncludedCustomLists = new[] { "Fixture included" };
                    break;
                case "excluded-list":
                    settings.ExcludedCustomLists = new[] { "Fixture excluded" };
                    break;
                case "included-status":
                    settings.ImportCompleted = true;
                    break;
                case "excluded-status":
                    settings.ExcludePlanning = true;
                    break;
                case "media-status":
                    settings.ImportCancelled = true;
                    break;
                case "base-url":
                    settings.BaseUrl = "https://fixture.invalid";
                    break;
                case "root-folder":
                    definition.RootFolderPath = "/different-fixture-library";
                    break;
                case "tags":
                    definition.Tags.Add(99);
                    break;
            }

            Subject.Fetch().Series.Should().BeEmpty();

            list.Verify(provider => provider.Fetch(), Times.Once());
            Mocker.GetMock<IImportListItemService>().Verify(service => service.GetAllForLists(It.IsAny<List<int>>()), Times.Never());
        }

        [Test]
        public void should_never_resume_saved_rows_that_were_not_witnessed_as_a_complete_fetch()
        {
            var list = WithAniList();
            _aniListStatus.LastInfoSync = DateTime.UtcNow;
            _savedAniListItems.Add(new ImportListItemInfo { ImportListId = 10, AniListId = 900101 });

            Subject.Fetch().Series.Should().BeEmpty();

            list.Verify(provider => provider.Fetch(), Times.Never());
            Mocker.GetMock<IImportListItemService>().Verify(service => service.GetAllForLists(It.IsAny<List<int>>()), Times.Never());
        }

        [Test]
        public void should_not_mark_a_partial_or_unknown_membership_response_as_resumable()
        {
            var list = WithAniList();
            list.Setup(provider => provider.Fetch()).Returns(new ImportListFetchResult(new[] { new ImportListItemInfo { AniListId = 900101 } }, true));

            Subject.Fetch().AnyFailure.Should().BeTrue();
            _aniListStatus.LastInfoSync = DateTime.UtcNow;
            Subject.Fetch().Series.Should().BeEmpty();

            Mocker.GetMock<IImportListItemService>().Verify(service => service.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), 10), Times.Never());
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.UpdateListSyncStatus(10, It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_invalidate_an_earlier_complete_witness_before_a_failed_manual_fetch()
        {
            var list = WithAniList();
            Subject.Fetch();
            list.Setup(provider => provider.Fetch()).Returns(new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), true));
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.GetInstance(It.IsAny<ImportListDefinition>())).Returns(list.Object);

            Subject.FetchSingleList((ImportListDefinition)list.Object.Definition).AnyFailure.Should().BeTrue();
            Subject.Fetch().Series.Should().BeEmpty();

            _savedAniListItems.Should().ContainSingle();
            Mocker.GetMock<IImportListItemService>().Verify(service => service.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), 10), Times.Once());
        }

        [Test]
        public void should_invalidate_an_earlier_complete_witness_before_a_throwing_manual_fetch()
        {
            var list = WithAniList();
            Subject.Fetch();
            list.Setup(provider => provider.Fetch()).Throws(new InvalidOperationException("Fixture fetch failure"));
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.GetInstance(It.IsAny<ImportListDefinition>())).Returns(list.Object);

            Subject.FetchSingleList((ImportListDefinition)list.Object.Definition).AnyFailure.Should().BeTrue();
            Subject.Fetch().Series.Should().BeEmpty();

            _savedAniListItems.Should().ContainSingle();
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_clear_saved_membership_after_a_complete_empty_response()
        {
            var list = WithAniList();
            Subject.Fetch();
            list.Setup(provider => provider.Fetch()).Returns(new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), false));
            Mocker.GetMock<IImportListFactory>().Setup(factory => factory.GetInstance(It.IsAny<ImportListDefinition>())).Returns(list.Object);

            Subject.FetchSingleList((ImportListDefinition)list.Object.Definition).Series.Should().BeEmpty();
            Subject.Fetch().Series.Should().BeEmpty();

            _savedAniListItems.Should().BeEmpty();
            _aniListStatus.HasRemovedItemSinceLastClean.Should().BeTrue();
            list.Verify(provider => provider.Fetch(), Times.Exactly(2));
        }

        [TestCase("disabled")]
        [TestCase("expired-disabled-flag")]
        [TestCase("newer-failure")]
        [TestCase("equal-failure")]
        [TestCase("different-sync")]
        [TestCase("automatic-add-disabled")]
        public void should_refuse_a_witness_that_no_longer_matches_successful_unblocked_status(string change)
        {
            var list = WithAniList();
            Subject.Fetch();

            switch (change)
            {
                case "disabled":
                    _aniListStatus.DisabledTill = DateTime.UtcNow.AddHours(1);
                    break;
                case "expired-disabled-flag":
                    _aniListStatus.DisabledTill = DateTime.UtcNow.AddHours(-1);
                    break;
                case "newer-failure":
                    _aniListStatus.MostRecentFailure = _aniListStatus.LastInfoSync.Value.AddSeconds(1);
                    break;
                case "equal-failure":
                    _aniListStatus.MostRecentFailure = _aniListStatus.LastInfoSync;
                    break;
                case "different-sync":
                    _aniListStatus.LastInfoSync = _aniListStatus.LastInfoSync.Value.AddSeconds(-1);
                    break;
                case "automatic-add-disabled":
                    ((ImportListDefinition)list.Object.Definition).EnableAutomaticAdd = false;
                    break;
            }

            Subject.Fetch().Series.Should().BeEmpty();

            list.Verify(provider => provider.Fetch(), Times.Once());
            Mocker.GetMock<IImportListItemService>().Verify(service => service.GetAllForLists(It.IsAny<List<int>>()), Times.Never());
        }

        [Test]
        public void should_resume_authoritative_database_rows_with_no_anilist_season_monitor_overrides()
        {
            WithAniList();
            Subject.Fetch();
            _savedAniListItems = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { ImportListId = 10, AniListId = 900102, Title = "Saved fixture", Seasons = new List<Season> { new Season { SeasonNumber = 2, Monitored = true } } },
                new ImportListItemInfo { ImportListId = 99, AniListId = 900999, Title = "Other list fixture" }
            };

            var resumed = Subject.Fetch().Series.Should().ContainSingle().Subject;

            resumed.AniListId.Should().Be(900102);
            resumed.ImportListId.Should().Be(10);
            resumed.ImportList.Should().Be("Fixture AniList");
            resumed.Seasons.Should().BeEmpty();
            _savedAniListItems[0].Seasons.Should().ContainSingle();
        }

        [Test]
        public void should_not_witness_a_success_without_the_post_sync_status_timestamp()
        {
            var list = WithAniList();
            Mocker.GetMock<IImportListStatusService>()
                  .Setup(service => service.UpdateListSyncStatus(10, It.IsAny<bool>()));
            Subject.Fetch();
            _aniListStatus.LastInfoSync = DateTime.UtcNow;

            Subject.Fetch().Series.Should().BeEmpty();

            list.Verify(provider => provider.Fetch(), Times.Once());
        }

        [Test]
        public void should_skip_recently_fetched_list()
        {
            var fetchResult = new ImportListFetchResult();
            var list = WithList(1, true, true, fetchResult, lastSyncOffset: 0);

            var result = Subject.Fetch();

            list.Verify(f => f.Fetch(), Times.Never());
            result.Series.Count.Should().Be(0);
            result.AnyFailure.Should().BeFalse();
        }

        [Test]
        public void should_skip_recent_and_fetch_good()
        {
            var fetchResult = new ImportListFetchResult();
            var recent = WithList(1, true, true, fetchResult, lastSyncOffset: 0);
            var old = WithList(2, true, true, fetchResult);

            var result = Subject.Fetch();

            recent.Verify(f => f.Fetch(), Times.Never());
            old.Verify(f => f.Fetch(), Times.Once());
            result.AnyFailure.Should().BeFalse();
        }

        [Test]
        public void should_return_failure_if_single_list_fails()
        {
            var fetchResult = new ImportListFetchResult { Series = _listSeries, AnyFailure = true };
            WithList(1, true, true, fetchResult);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_preserve_distinct_anilist_entries_with_identical_titles()
        {
            var items = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { AniListId = 1, Title = "Same title" },
                new ImportListItemInfo { AniListId = 9253, Title = "Same title" }
            };
            var fetchResult = new ImportListFetchResult(items, false);
            WithList(1, true, true, fetchResult);

            Subject.Fetch().Series.Select(item => item.AniListId).Should().BeEquivalentTo(new[] { 1, 9253 });
        }

        [Test]
        public void should_preserve_each_list_membership_for_the_same_anilist_entry()
        {
            WithList(1, true, true, new ImportListFetchResult(new[] { new ImportListItemInfo { AniListId = 21, Title = "One Piece" } }, false));
            WithList(2, true, true, new ImportListFetchResult(new[] { new ImportListItemInfo { AniListId = 21, Title = "One Piece" } }, false));

            Subject.Fetch().Series.Select(item => item.ImportListId).Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void should_record_failure_when_a_provider_throws_before_returning_a_result()
        {
            var list = WithList(1, true, true, new ImportListFetchResult());
            list.Setup(provider => provider.Fetch()).Throws(new InvalidOperationException("Authentication failed"));

            Subject.Fetch().AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListStatusService>().Verify(service => service.RecordFailure(1, TimeSpan.Zero), Times.Once());
            Mocker.GetMock<IImportListStatusService>().Verify(service => service.UpdateListSyncStatus(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IImportListItemService>().Verify(service => service.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), 1), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_return_failure_if_any_list_fails()
        {
            var fetchResult1 = new ImportListFetchResult { Series = _listSeries, AnyFailure = true };
            WithList(1, true, true, fetchResult1);
            var fetchResult2 = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(2, true, true, fetchResult2);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeTrue();
        }

        [Test]
        public void should_return_early_if_no_available_lists()
        {
            var listResult = Subject.Fetch();

            Mocker.GetMock<IImportListStatusService>()
                  .Verify(v => v.GetListStatus(It.IsAny<int>()), Times.Never());

            listResult.Series.Count.Should().Be(0);
            listResult.AnyFailure.Should().BeFalse();
        }

        [Test]
        public void should_store_series_if_list_doesnt_fail()
        {
            var listId = 1;
            var fetchResult = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(listId, true, true, fetchResult);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeFalse();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(listId, false), Times.Once());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncSeriesForList(_listSeries, listId), Times.Once());
        }

        [Test]
        public void should_not_store_series_if_list_fails()
        {
            var listId = 1;
            var fetchResult = new ImportListFetchResult { Series = _listSeries, AnyFailure = true };
            WithList(listId, true, true, fetchResult);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(listId, false), Times.Never());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), listId), Times.Never());
        }

        [Test]
        public void should_only_store_series_for_lists_that_dont_fail()
        {
            var passedListId = 1;
            var fetchResult1 = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(passedListId, true, true, fetchResult1);
            var failedListId = 2;
            var fetchResult2 = new ImportListFetchResult { Series = _listSeries, AnyFailure = true };
            WithList(failedListId, true, true, fetchResult2);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeTrue();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(passedListId, false), Times.Once());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncSeriesForList(_listSeries, passedListId), Times.Once());
            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(failedListId, false), Times.Never());
            Mocker.GetMock<IImportListItemService>()
                .Verify(v => v.SyncSeriesForList(It.IsAny<List<ImportListItemInfo>>(), failedListId), Times.Never());
        }

        [Test]
        public void should_return_all_results_for_all_lists()
        {
            var passedListId = 1;
            var fetchResult1 = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(passedListId, true, true, fetchResult1);
            var secondListId = 2;
            var fetchResult2 = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(secondListId, true, true, fetchResult2);

            var listResult = Subject.Fetch();
            listResult.AnyFailure.Should().BeFalse();
            listResult.Series.Count.Should().Be(5);
        }

        [Test]
        public void should_set_removed_flag_if_list_has_removed_items()
        {
            var listId = 1;
            var fetchResult = new ImportListFetchResult { Series = _listSeries, AnyFailure = false };
            WithList(listId, true, true, fetchResult, syncDeletedCount: 500);

            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();

            Mocker.GetMock<IImportListStatusService>()
                .Verify(v => v.UpdateListSyncStatus(listId, true), Times.Once());
        }
    }
}
