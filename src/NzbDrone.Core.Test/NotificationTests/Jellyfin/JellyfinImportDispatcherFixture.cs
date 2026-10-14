using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;
using JellyfinNotification = NzbDrone.Core.Notifications.Jellyfin.Jellyfin;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinImportDispatcherFixture : DbTest
    {
        private JellyfinSettings _settings;
        private NotificationDefinition _definition;
        private JellyfinRefreshStateRepository _repository;
        private NotificationService _dispatcher;
        private LocalEpisode _localEpisode;

        private JellyfinRefreshState State => _repository.ForNotification(_definition.Id);

        [SetUp]
        public void Setup()
        {
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example",
                ApiKey = "test-key",
                LibraryId = "d834474f444b4d328119fe8c4b9c71fa",
                RefreshMode = (int)JellyfinRefreshMode.Interval
            };
            _definition = new NotificationDefinition
            {
                Id = 10,
                Name = "Anime Jellyfin",
                Implementation = "Jellyfin",
                OnDownload = true,
                OnUpgrade = true,
                Settings = _settings
            };
            _localEpisode = new LocalEpisode
            {
                Series = new Series { Id = 1, Title = "Test series", Tags = new HashSet<int>() },
                Episodes = new List<Episode> { new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 1, Title = "Test episode" } },
                Quality = new QualityModel(),
                Path = "Test.Series.S01E01.mkv"
            };

            Mocker.GetMock<INotificationRepository>().Setup(repository => repository.Find(10)).Returns(_definition);
            Mocker.GetMock<INotificationRepository>().Setup(repository => repository.All()).Returns(new[] { _definition });
            Mocker.GetMock<IJellyfinRefreshClock>().SetupGet(clock => clock.UtcNow).Returns(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc));
            _repository = Mocker.Resolve<JellyfinRefreshStateRepository>();
            Mocker.SetConstant<IJellyfinRefreshStateRepository>(_repository);
            var refreshService = Mocker.Resolve<JellyfinRefreshService>();
            refreshService.ProcessPending();
            var notification = new JellyfinNotification(Mocker.GetMock<IJellyfinProxy>().Object, refreshService, Mocker.GetMock<IJellyfinWatchProxy>().Object, Mocker.GetMock<INotificationRepository>().Object)
            {
                Definition = _definition
            };

            Mocker.GetMock<INotificationFactory>()
                .Setup(factory => factory.OnDownloadEnabled(true))
                .Returns(new List<INotification> { notification });
            _dispatcher = Mocker.Resolve<NotificationService>();
        }

        private EpisodeImportedEvent ImportedEvent(bool newDownload, bool upgrade = false)
        {
            var oldFiles = new List<DeletedEpisodeFile>();

            if (upgrade)
            {
                oldFiles.Add(new DeletedEpisodeFile(new EpisodeFile { Id = 99 }, null));
            }

            return new EpisodeImportedEvent(_localEpisode, new EpisodeFile { Id = 100 }, oldFiles, newDownload, null);
        }

        [Test]
        public void should_dispatch_a_new_download_or_new_manual_file_import_into_the_durable_queue()
        {
            // Both downloaded imports and manual imports of new files publish this event
            // with NewDownload=true; no transport callback is called directly by this test.
            _dispatcher.Handle(ImportedEvent(true));

            State.ImportedGeneration.Should().Be(1);
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(10), Times.Once());
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_not_enqueue_a_rescan_or_existing_manual_file_import()
        {
            _dispatcher.Handle(ImportedEvent(false));

            State.ImportedGeneration.Should().Be(0);
            Mocker.GetMock<INotificationFactory>().Verify(factory => factory.OnDownloadEnabled(true), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_dispatch_upgrades_only_when_the_upgrade_trigger_is_enabled(bool onUpgrade)
        {
            _definition.OnUpgrade = onUpgrade;

            _dispatcher.Handle(ImportedEvent(true, true));

            State.ImportedGeneration.Should().Be(onUpgrade ? 1 : 0);
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(10), onUpgrade ? Times.Once() : Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_dispatch_tagged_series_only_when_the_connection_tags_match(bool match)
        {
            _definition.Tags.Add(5);
            _localEpisode.Series.Tags.Add(match ? 5 : 6);

            _dispatcher.Handle(ImportedEvent(true));

            State.ImportedGeneration.Should().Be(match ? 1 : 0);
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(10), match ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_preserve_a_failed_immediate_refresh_without_blocking_the_notification_provider()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            Mocker.GetMock<IJellyfinProxy>().Setup(proxy => proxy.RefreshLibrary(_settings)).Throws(new Exception("offline"));

            _dispatcher.Handle(ImportedEvent(true));

            State.ImportedGeneration.Should().Be(1);
            State.ImmediateGeneration.Should().Be(0);
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordSuccess(10), Times.Once());
            Mocker.GetMock<INotificationStatusService>().Verify(status => status.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
