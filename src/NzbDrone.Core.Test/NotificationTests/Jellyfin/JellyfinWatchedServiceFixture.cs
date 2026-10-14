using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinWatchedServiceFixture : DbTest
    {
        private JellyfinSettings _settings;
        private NotificationDefinition _definition;
        private List<NotificationDefinition> _definitions;
        private List<JellyfinWatchedItem> _snapshot;
        private JellyfinWatchedService _subject;
        private JellyfinWatchStateRepository _states;
        private EpisodeRepository _episodes;
        private SeriesRepository _series;
        private Series _show;
        private Episode _episode;
        private DateTime _now;

        private Episode Stored => _episodes.Get(_episode.Id);

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example", ApiKey = "private-fixture-key",
                LibraryId = "d834474f444b4d328119fe8c4b9c71fa",
                WatchedUserId = "3d88a080dc9b4983b10b18d21fa69e6b", EnableWatchedSync = true,
                WatchedSyncIntervalMinutes = 5
            };
            _definition = new NotificationDefinition { Id = 10, Implementation = "Jellyfin", Settings = _settings };
            _definitions = new List<NotificationDefinition> { _definition };
            Mocker.GetMock<INotificationRepository>().Setup(repository => repository.All()).Returns(() => _definitions.ToList());
            Mocker.GetMock<INotificationRepository>().Setup(repository => repository.Find(10)).Returns(() => _definitions.SingleOrDefault(value => value.Id == 10));
            Mocker.GetMock<IJellyfinRefreshClock>().SetupGet(clock => clock.UtcNow).Returns(() => _now);

            _states = Mocker.Resolve<JellyfinWatchStateRepository>();
            _episodes = Mocker.Resolve<EpisodeRepository>();
            _series = Mocker.Resolve<SeriesRepository>();
            var files = Mocker.Resolve<MediaFileRepository>();
            Mocker.SetConstant<IJellyfinWatchStateRepository>(_states);
            Mocker.SetConstant<IEpisodeRepository>(_episodes);
            Mocker.SetConstant<IMediaFileRepository>(files);
            Mocker.SetConstant<ISeriesRepository>(_series);
            _show = _series.Insert(new Series
            {
                Title = "Grouped anime", TvdbId = -90001, Path = "/media/Grouped Anime",
                CleanTitle = "groupedanime", SortTitle = "grouped anime", TitleSlug = "grouped-anime",
                Monitored = true, Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } },
                AnimeGroupId = "fixture:group", AnimeGroupSeasonNumber = 3
            });
            var file = files.Insert(new EpisodeFile
            {
                SeriesId = _show.Id, SeasonNumber = 1, RelativePath = "Season 03/Grouped Anime - S03E01.mkv",
                DateAdded = _now, Quality = new QualityModel(), Languages = new List<Language> { Language.English }
            });
            _episode = _episodes.Insert(new Episode
            {
                SeriesId = _show.Id, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = file.Id,
                Monitored = true, Title = "Generated episode"
            });
            _snapshot = new List<JellyfinWatchedItem>
            {
                new JellyfinWatchedItem
                {
                    Id = "af7440e699464d958939fd71fe016d91", Type = "Episode",
                    Path = "/media/Grouped Anime/Season 03/Grouped Anime - S03E01.mkv",
                    UserData = new JellyfinWatchedUserData { Played = true }
                }
            };
            Mocker.GetMock<IJellyfinWatchProxy>().Setup(proxy => proxy.GetSnapshot(It.IsAny<JellyfinSettings>())).Returns(() => _snapshot);
            _subject = Mocker.Resolve<JellyfinWatchedService>();
        }

        [Test]
        public void should_save_watched_native_episodes_by_import_path_without_changing_group_numbering_or_monitoring()
        {
            _subject.Sync();
            var actual = Stored;
            actual.Watched.Should().BeTrue();
            actual.WatchedAtUtc.Should().Be(_now);
            actual.Monitored.Should().BeTrue();
            actual.SeasonNumber.Should().Be(1);
            _series.Get(_show.Id).AnimeGroupSeasonNumber.Should().Be(3);
            _definition.Enable.Should().BeTrue("watched-only Jellyfin connections are active without import notifications");
            Mocker.GetMock<IImportListProgressService>().Verify(service => service.SyncProgress(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Func<bool>>()), Times.Never());
        }

        [Test]
        public void should_be_disabled_by_default_and_not_contact_jellyfin_when_disabled()
        {
            new JellyfinSettings().EnableWatchedSync.Should().BeFalse();
            _settings.EnableWatchedSync = false;
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            _states.All().Should().BeEmpty();
            Mocker.GetMock<IJellyfinWatchProxy>().Verify(proxy => proxy.GetSnapshot(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_persist_the_cadence_across_service_restart_and_allow_manual_sync()
        {
            _subject.Sync();
            _now = _now.AddMinutes(4);
            Mocker.Resolve<JellyfinWatchedService>().Sync();
            Mocker.GetMock<IJellyfinWatchProxy>().Verify(proxy => proxy.GetSnapshot(_settings), Times.Once());
            _subject.Sync(true);
            Mocker.GetMock<IJellyfinWatchProxy>().Verify(proxy => proxy.GetSnapshot(_settings), Times.Exactly(2));
            _now = _now.AddMinutes(5);
            _subject.Sync();
            Mocker.GetMock<IJellyfinWatchProxy>().Verify(proxy => proxy.GetSnapshot(_settings), Times.Exactly(3));
            Stored.WatchedAtUtc.Should().Be(_now.AddMinutes(-9));
        }

        [Test]
        public void should_unmonitor_and_remonitor_only_its_own_prior_changes()
        {
            _settings.UnmonitorWatchedEpisodes = true;
            _settings.RemonitorUnwatchedEpisodes = true;
            _subject.Sync(true);
            Stored.Monitored.Should().BeFalse();
            Stored.JellyfinUnmonitorOwner.Should().NotBeNullOrEmpty();
            _snapshot[0].UserData.Played = false;
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            Stored.WatchedAtUtc.Should().BeNull();
            Stored.Monitored.Should().BeTrue();
            Stored.JellyfinUnmonitorOwner.Should().BeNull();
        }

        [Test]
        public void should_preserve_manually_unmonitored_episodes()
        {
            _settings.UnmonitorWatchedEpisodes = true;
            _settings.RemonitorUnwatchedEpisodes = true;
            _episodes.SetMonitoredFlat(Stored, false);
            _subject.Sync(true);
            Stored.JellyfinUnmonitorOwner.Should().BeNull();
            _snapshot[0].UserData.Played = false;
            _subject.Sync(true);
            Stored.Monitored.Should().BeFalse();
        }

        [Test]
        public void should_preserve_manual_remonitor_of_an_already_watched_episode_on_unchanged_polls()
        {
            _settings.UnmonitorWatchedEpisodes = true;
            _subject.Sync(true);
            Stored.Monitored.Should().BeFalse();
            _episodes.SetMonitoredFlat(Stored, true);
            _subject.Sync(true);
            Stored.Watched.Should().BeTrue();
            Stored.Monitored.Should().BeTrue();
            Stored.JellyfinUnmonitorOwner.Should().BeNull();
        }

        [Test]
        public void should_apply_a_newly_enabled_unmonitor_policy_once_and_preserve_later_manual_choices()
        {
            _subject.Sync(true);
            Stored.Monitored.Should().BeTrue();
            _settings.UnmonitorWatchedEpisodes = true;
            _subject.Sync(true);
            Stored.Monitored.Should().BeFalse();
            _episodes.SetMonitoredFlat(Stored, true);
            _subject.Sync(true);
            Stored.Monitored.Should().BeTrue();
        }

        [TestCase("episode")]
        [TestCase("bulk")]
        [TestCase("season")]
        [TestCase("series")]
        public void should_clear_ownership_even_when_manual_unmonitor_repeats_the_current_flag(string mode)
        {
            _settings.UnmonitorWatchedEpisodes = true;
            _settings.RemonitorUnwatchedEpisodes = true;
            _subject.Sync(true);
            if (mode == "episode")
            {
                _episodes.SetMonitoredFlat(Stored, false);
            }
            else if (mode == "bulk")
            {
                _episodes.SetMonitored(new[] { _episode.Id }, false);
            }
            else if (mode == "season")
            {
                _episodes.SetMonitoredBySeason(_show.Id, 1, false);
            }
            else
            {
                _episodes.SetMonitored(_show.Id, MonitorTypes.None, 1, 1);
            }

            Stored.JellyfinUnmonitorOwner.Should().BeNull();
            _snapshot[0].UserData.Played = false;
            _subject.Sync(true);
            Stored.Monitored.Should().BeFalse();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_not_remonitor_a_manually_unmonitored_series_or_season(bool series)
        {
            _settings.UnmonitorWatchedEpisodes = true;
            _settings.RemonitorUnwatchedEpisodes = true;
            _subject.Sync(true);
            _show.Monitored = !series;
            _show.Seasons[0].Monitored = series;
            _series.Update(_show);
            _snapshot[0].UserData.Played = false;
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            Stored.Monitored.Should().BeFalse();
        }

        [Test]
        public void should_retain_previous_state_for_absent_and_unmatched_items()
        {
            _subject.Sync(true);
            _snapshot.Clear();
            _subject.Sync(true);
            Stored.Watched.Should().BeTrue();
            _snapshot.Add(new JellyfinWatchedItem
            {
                Path = "/media/Grouped Anime/Season 03/A different file.mkv",
                UserData = new JellyfinWatchedUserData { Played = false }
            });
            _subject.Sync(true);
            Stored.Watched.Should().BeTrue();
        }

        [Test]
        public void should_retain_the_previous_watermark_and_retry_after_network_failure()
        {
            _subject.Sync();
            var successful = _states.ForNotification(10).LastSyncUtc;
            _now = _now.AddMinutes(5);
            Mocker.GetMock<IJellyfinWatchProxy>().Setup(proxy => proxy.GetSnapshot(_settings)).Throws(new InvalidOperationException("private-fixture-key response"));
            _subject.Sync();
            Stored.Watched.Should().BeTrue();
            _states.ForNotification(10).LastSyncUtc.Should().Be(successful);
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IJellyfinWatchProxy>().Setup(proxy => proxy.GetSnapshot(_settings)).Returns(_snapshot);
            _subject.Sync();
            _states.ForNotification(10).LastSyncUtc.Should().Be(_now);
        }

        [Test]
        public void should_reject_conflicting_paths_before_writing_any_local_state()
        {
            _snapshot.Add(new JellyfinWatchedItem
            {
                Path = _snapshot[0].Path, UserData = new JellyfinWatchedUserData { Played = false }
            });
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            _states.All().Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_apply_a_snapshot_when_settings_change_while_the_request_is_in_flight()
        {
            Mocker.GetMock<IJellyfinWatchProxy>().Setup(proxy => proxy.GetSnapshot(_settings)).Returns(() =>
            {
                _definition.Settings = new JellyfinSettings { EnableWatchedSync = false };
                return _snapshot;
            });
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            _states.All().Should().BeEmpty();
        }

        [Test]
        public void should_refuse_to_mix_watched_users_from_two_enabled_connections()
        {
            _definitions.Add(new NotificationDefinition { Id = 11, Implementation = "Jellyfin", Settings = _settings });
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            Mocker.GetMock<IJellyfinWatchProxy>().Verify(proxy => proxy.GetSnapshot(It.IsAny<JellyfinSettings>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_sync_exact_current_user_native_episode_ids_and_retry_origin_progress_after_unchanged_snapshots()
        {
            _settings.SyncListProgress = true;
            _subject.Sync(true);
            _subject.Sync(true);
            Mocker.GetMock<IImportListProgressService>().Verify(service => service.SyncProgress(_show.Id, It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(_episode.Id)), It.IsAny<Func<bool>>()), Times.Exactly(2));
        }

        [Test]
        public void should_save_watched_state_before_origin_write_failure()
        {
            _settings.SyncListProgress = true;
            Mocker.GetMock<IImportListProgressService>().Setup(service => service.SyncProgress(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Func<bool>>())).Throws(new InvalidOperationException("private origin response"));
            _subject.Sync(true);
            Stored.Watched.Should().BeTrue();
            _states.ForNotification(10).LastSyncUtc.Should().Be(_now);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_revoke_later_origin_target_authorization_when_global_progress_is_disabled_during_the_first_advance()
        {
            _settings.SyncListProgress = true;
            var permissions = new List<bool>();
            Mocker.GetMock<IImportListProgressService>()
                .Setup(service => service.SyncProgress(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Func<bool>>()))
                .Callback<int, IReadOnlyCollection<int>, Func<bool>>((seriesId, watched, canDispatch) =>
                {
                    permissions.Add(canDispatch());
                    _settings.SyncListProgress = false;
                    permissions.Add(canDispatch());
                });

            _subject.Sync(true);

            permissions.Should().Equal(true, false);
            Stored.Watched.Should().BeTrue("a later origin opt-out does not erase successfully read local watch history");
            _states.ForNotification(10).LastSyncUtc.Should().Be(_now);
        }

        [TestCase("disabled")]
        [TestCase("user")]
        [TestCase("library")]
        [TestCase("paths")]
        [TestCase("policy")]
        [TestCase("notification-tags")]
        [TestCase("series-tags")]
        [TestCase("second-connection")]
        [TestCase("deleted")]
        [TestCase("read-failure")]
        public void should_recheck_immutable_notification_scope_before_each_later_origin_target(string change)
        {
            _settings.SyncListProgress = true;
            if (change == "series-tags")
            {
                _definition.Tags.Add(9);
                _show.Tags.Add(9);
                _series.Update(_show);
            }

            var permissions = new List<bool>();
            Mocker.GetMock<IImportListProgressService>()
                .Setup(service => service.SyncProgress(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Func<bool>>()))
                .Callback<int, IReadOnlyCollection<int>, Func<bool>>((seriesId, watched, canDispatch) =>
                {
                    permissions.Add(canDispatch());
                    switch (change)
                    {
                        case "disabled":
                            _settings.EnableWatchedSync = false;
                            break;
                        case "user":
                            _settings.WatchedUserId = "af7440e699464d958939fd71fe016d91";
                            break;
                        case "library":
                            _settings.LibraryId = "af7440e699464d958939fd71fe016d91";
                            break;
                        case "paths":
                            _settings.JellyfinPathPrefix = "/media";
                            _settings.TanukarrPathPrefix = "/media";
                            break;
                        case "policy":
                            _settings.UnmonitorWatchedEpisodes = true;
                            break;
                        case "notification-tags":
                            _definition.Tags.Add(9);
                            break;
                        case "series-tags":
                            _show.Tags.Clear();
                            _series.Update(_show);
                            break;
                        case "second-connection":
                            _definitions.Add(new NotificationDefinition { Id = 11, Implementation = "Jellyfin", Settings = _settings });
                            break;
                        case "deleted":
                            _definitions.Clear();
                            break;
                        case "read-failure":
                            Mocker.GetMock<INotificationRepository>().Setup(repository => repository.Find(10)).Throws(new InvalidOperationException("private failed permission read"));
                            break;
                    }

                    permissions.Add(canDispatch());
                });

            _subject.Sync(true);

            permissions.Should().Equal(true, false);
            Stored.Watched.Should().BeTrue();
        }

        [Test]
        public void should_not_call_progress_for_a_later_series_after_global_writeback_is_disabled()
        {
            _settings.SyncListProgress = true;
            var otherShow = _series.Insert(new Series
            {
                Title = "Other generated series", TvdbId = -90002, Path = "/media/Other",
                CleanTitle = "othergeneratedseries", SortTitle = "other generated series", TitleSlug = "other-generated-series",
                Monitored = true, Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } }
            });
            var files = Mocker.Resolve<MediaFileRepository>();
            var otherFile = files.Insert(new EpisodeFile
            {
                SeriesId = otherShow.Id, SeasonNumber = 1, RelativePath = "Episode.mkv",
                DateAdded = _now, Quality = new QualityModel(), Languages = new List<Language> { Language.English }
            });
            _episodes.Insert(new Episode
            {
                SeriesId = otherShow.Id, SeasonNumber = 1, EpisodeNumber = 1, EpisodeFileId = otherFile.Id,
                Monitored = true, Title = "Other generated episode"
            });
            _snapshot.Add(new JellyfinWatchedItem
            {
                Id = "3d88a080dc9b4983b10b18d21fa69e6b", Type = "Episode", Path = "/media/Other/Episode.mkv",
                UserData = new JellyfinWatchedUserData { Played = true }
            });
            var calls = 0;
            Mocker.GetMock<IImportListProgressService>()
                .Setup(service => service.SyncProgress(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<Func<bool>>()))
                .Callback<int, IReadOnlyCollection<int>, Func<bool>>((seriesId, watched, canDispatch) =>
                {
                    canDispatch().Should().BeTrue();
                    calls++;
                    _settings.SyncListProgress = false;
                });

            _subject.Sync(true);

            calls.Should().Be(1);
            _episodes.GetEpisodes(otherShow.Id).Single().Watched.Should().BeTrue();
        }

        [Test]
        public void should_apply_connection_tags_and_never_guess_from_matching_titles()
        {
            _definition.Tags.Add(9);
            _subject.Sync(true);
            Stored.Watched.Should().BeFalse();
            _show.Tags.Add(9);
            _series.Update(_show);
            _subject.Sync(true);
            Stored.Watched.Should().BeTrue();
        }

        [Test]
        public void should_release_deleted_connection_state_without_remonitoring_or_clearing_history()
        {
            _subject.Sync(true);
            _subject.Handle(new ProviderDeletedEvent<INotification>(10));
            _states.All().Should().BeEmpty();
            Stored.Watched.Should().BeTrue();
        }
    }
}
