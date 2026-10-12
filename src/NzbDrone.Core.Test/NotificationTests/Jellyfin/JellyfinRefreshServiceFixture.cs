using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinRefreshServiceFixture : DbTest
    {
        private DateTime _now;
        private List<NotificationDefinition> _definitions;
        private NotificationDefinition _definition;
        private JellyfinSettings _settings;
        private JellyfinRefreshStateRepository _repository;
        private JellyfinRefreshService _subject;

        private JellyfinRefreshState State => _repository.ForNotification(_definition.Id);

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
            _settings = new JellyfinSettings
            {
                Host = "jellyfin.example",
                ApiKey = "private-test-key",
                LibraryId = "d834474f444b4d328119fe8c4b9c71fa",
                RefreshMode = (int)JellyfinRefreshMode.Interval,
                RefreshIntervalMinutes = 60
            };

            _definition = Definition(10, _settings);
            _definitions = new List<NotificationDefinition> { _definition };

            Mocker.GetMock<INotificationRepository>()
                .Setup(repository => repository.All())
                .Returns(() => _definitions.ToList());
            Mocker.GetMock<INotificationRepository>()
                .Setup(repository => repository.Find(It.IsAny<int>()))
                .Returns((int id) => _definitions.SingleOrDefault(definition => definition.Id == id));
            Mocker.GetMock<IJellyfinRefreshClock>()
                .SetupGet(clock => clock.UtcNow)
                .Returns(() => _now);

            // Use SQLite, including independent repository reads and writes, rather than
            // a mutable mocked state object that could hide missing persistence.
            _repository = Mocker.Resolve<JellyfinRefreshStateRepository>();
            Mocker.SetConstant<IJellyfinRefreshStateRepository>(_repository);
            _subject = Mocker.Resolve<JellyfinRefreshService>();
            _subject.ProcessPending();
        }

        private static NotificationDefinition Definition(int id, JellyfinSettings settings)
        {
            return new NotificationDefinition
            {
                Id = id,
                Name = "Jellyfin connection " + id,
                Implementation = "Jellyfin",
                OnDownload = true,
                OnUpgrade = true,
                Settings = settings
            };
        }

        private void AdvanceMinutes(double minutes)
        {
            _now = _now.AddMinutes(minutes);
        }

        [Test]
        public void should_not_contact_jellyfin_on_idle_ticks()
        {
            AdvanceMinutes(60);
            _subject.ProcessPending();
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
            State.LastTickUtc.Should().Be(_now);
        }

        [TestCase(1)]
        [TestCase(60)]
        public void should_wait_until_the_configured_interval_before_refreshing_pending_imports(int minutes)
        {
            _settings.RefreshIntervalMinutes = minutes;
            _subject.OnImport(_definition);

            AdvanceMinutes(minutes - (1.0 / 60));
            _subject.ProcessPending();
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Never());

            _now = _now.AddSeconds(1);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
            State.TickGeneration.Should().Be(1);
        }

        [Test]
        public void should_coalesce_a_burst_into_one_selected_library_refresh_per_tick()
        {
            for (var index = 0; index < 25; index++)
            {
                _subject.OnImport(_definition);
            }

            AdvanceMinutes(60);
            _subject.ProcessPending();
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
            State.ImportedGeneration.Should().Be(25);
            State.TickGeneration.Should().Be(25);
        }

        [Test]
        public void should_measure_new_imports_from_the_previous_idle_tick()
        {
            AdvanceMinutes(60);
            _subject.ProcessPending();
            AdvanceMinutes(20);
            _subject.OnImport(_definition);
            AdvanceMinutes(39);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Never());

            AdvanceMinutes(1);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
        }

        [Test]
        public void should_refresh_each_import_immediately_without_an_extra_idle_interval_refresh()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;

            _subject.OnImport(_definition);
            _subject.OnImport(_definition);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
            State.ImmediateGeneration.Should().Be(2);
        }

        [Test]
        public void should_refresh_each_import_and_still_refresh_one_dirty_combined_tick()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImportAndInterval;

            _subject.OnImport(_definition);
            _subject.OnImport(_definition);
            State.TickGeneration.Should().Be(0);

            AdvanceMinutes(60);
            _subject.ProcessPending();
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(3));
            State.ImmediateGeneration.Should().Be(2);
            State.TickGeneration.Should().Be(2);
        }

        [Test]
        public void should_keep_a_failed_interval_refresh_pending_for_the_next_scheduler_attempt()
        {
            Mocker.GetMock<IJellyfinProxy>()
                .SetupSequence(proxy => proxy.RefreshLibrary(_settings))
                .Throws(new Exception("untrusted error containing " + _settings.ApiKey))
                .Pass();
            _subject.OnImport(_definition);
            AdvanceMinutes(60);

            _subject.ProcessPending();
            State.TickGeneration.Should().Be(0);
            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.TickGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_cold_library_imports_pending_until_a_later_initialized_refresh_succeeds()
        {
            var initialized = false;
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<List<JellyfinLibrary>>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<List<JellyfinLibrary>>(new HttpResponse(
                    request,
                    new HttpHeader(),
                    "[{\"Name\":\"Anime\",\"ItemId\":\"" + _settings.LibraryId + "\",\"Locations\":[\"/media/Anime\"]}]")));
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<JellyfinItems>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JellyfinItems>(new HttpResponse(
                    request,
                    new HttpHeader(),
                    initialized
                        ? "{\"Items\":[{\"Id\":\"af7440e699464d958939fd71fe016d91\",\"Path\":\"/media/Anime\",\"Type\":\"Folder\"}],\"TotalRecordCount\":1}"
                        : "{\"Items\":[],\"TotalRecordCount\":0}")));
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NoContent));
            _subject = new JellyfinRefreshService(
                _repository,
                Mocker.GetMock<INotificationRepository>().Object,
                new JellyfinProxy(Mocker.GetMock<IHttpClient>().Object),
                Mocker.GetMock<IJellyfinRefreshClock>().Object,
                TestLogger);
            _subject.OnImport(_definition);
            _subject.OnImport(_definition);
            AdvanceMinutes(60);

            _subject.ProcessPending();

            State.ImportedGeneration.Should().Be(2);
            State.ImmediateGeneration.Should().Be(0);
            State.TickGeneration.Should().Be(0);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
            initialized = true;
            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.ImportedGeneration.Should().Be(2);
            State.TickGeneration.Should().Be(2);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_failed_immediate_imports_without_throwing_to_notification_backoff()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            Mocker.GetMock<IJellyfinProxy>()
                .SetupSequence(proxy => proxy.RefreshLibrary(_settings))
                .Throws(new Exception("connection unavailable"))
                .Pass();

            Action action = () => _subject.OnImport(_definition);
            action.Should().NotThrow();
            State.ImmediateGeneration.Should().Be(0);

            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.ImmediateGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_acknowledge_a_failed_immediate_import_and_its_due_tick_with_one_retry()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImportAndInterval;
            Mocker.GetMock<IJellyfinProxy>()
                .SetupSequence(proxy => proxy.RefreshLibrary(_settings))
                .Throws(new Exception("connection unavailable"))
                .Pass();

            _subject.OnImport(_definition);
            AdvanceMinutes(60);
            _subject.ProcessPending();
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.ImmediateGeneration.Should().Be(1);
            State.TickGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_preserve_pending_work_and_the_tick_cadence_across_a_service_restart()
        {
            _subject.OnImport(_definition);
            _subject.OnImport(_definition);
            AdvanceMinutes(59);

            var restarted = new JellyfinRefreshService(
                _repository,
                Mocker.GetMock<INotificationRepository>().Object,
                Mocker.GetMock<IJellyfinProxy>().Object,
                Mocker.GetMock<IJellyfinRefreshClock>().Object,
                TestLogger);

            restarted.ProcessPending();
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Never());
            AdvanceMinutes(1);
            restarted.ProcessPending();

            _repository.All().Should().ContainSingle();
            State.ImportedGeneration.Should().Be(2);
            State.TickGeneration.Should().Be(2);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
        }

        [Test]
        public void should_not_lose_an_import_arriving_while_a_tick_request_is_in_flight()
        {
            var importedDuringRequest = false;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.RefreshLibrary(_settings))
                .Callback(() =>
                {
                    if (!importedDuringRequest)
                    {
                        importedDuringRequest = true;
                        _subject.OnImport(_definition);
                    }
                });

            _subject.OnImport(_definition);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.ImportedGeneration.Should().Be(2);
            State.TickGeneration.Should().Be(1);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.TickGeneration.Should().Be(2);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
        }

        [Test]
        public void should_persist_an_import_arriving_during_an_immediate_request_without_recursing()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            var importedDuringRequest = false;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.RefreshLibrary(_settings))
                .Callback(() =>
                {
                    if (!importedDuringRequest)
                    {
                        importedDuringRequest = true;
                        _subject.OnImport(_definition);
                    }
                });

            _subject.OnImport(_definition);

            State.ImportedGeneration.Should().Be(2);
            State.ImmediateGeneration.Should().Be(2);
            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.ImmediateGeneration.Should().Be(2);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
        }

        [Test]
        public async Task should_send_one_request_per_file_when_imports_arrive_during_an_immediate_request()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            var requestCount = 0;

            using (var firstRequest = new ManualResetEventSlim())
            using (var releaseRequest = new ManualResetEventSlim())
            {
                Mocker.GetMock<IJellyfinProxy>()
                    .Setup(proxy => proxy.RefreshLibrary(_settings))
                    .Callback(() =>
                    {
                        if (Interlocked.Increment(ref requestCount) == 1)
                        {
                            firstRequest.Set();

                            if (!releaseRequest.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException("The test did not release its first HTTP request.");
                            }
                        }
                    });

                var importing = Task.Run(() => _subject.OnImport(_definition));

                try
                {
                    firstRequest.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

                    for (var index = 0; index < 5; index++)
                    {
                        _subject.OnImport(_definition);
                    }

                    State.ImportedGeneration.Should().Be(6);
                    State.ImmediateGeneration.Should().Be(0);
                }
                finally
                {
                    releaseRequest.Set();
                }

                await importing.WaitAsync(TimeSpan.FromSeconds(10));

                State.ImmediateGeneration.Should().Be(6);
                requestCount.Should().Be(6);
            }
        }

        [Test]
        public void should_stop_an_immediate_burst_after_a_failure_and_retry_each_outstanding_import()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            var requestCount = 0;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.RefreshLibrary(_settings))
                .Callback(() =>
                {
                    if (++requestCount == 1)
                    {
                        for (var index = 0; index < 3; index++)
                        {
                            _subject.OnImport(_definition);
                        }

                        throw new Exception("first request failed");
                    }
                });

            _subject.OnImport(_definition);

            State.ImportedGeneration.Should().Be(4);
            State.ImmediateGeneration.Should().Be(0);
            requestCount.Should().Be(1);
            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.ImmediateGeneration.Should().Be(4);
            requestCount.Should().Be(5);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_replay_previous_successful_interval_imports_when_each_import_mode_is_selected()
        {
            for (var index = 0; index < 5; index++)
            {
                _subject.OnImport(_definition);
            }

            AdvanceMinutes(60);
            _subject.ProcessPending();
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
            State.ImmediateGeneration.Should().Be(5);

            _subject.OnImport(_definition);

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
            State.ImmediateGeneration.Should().Be(6);
        }

        [Test]
        public void should_not_replay_previous_successful_immediate_imports_when_interval_mode_is_selected()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;

            for (var index = 0; index < 5; index++)
            {
                _subject.OnImport(_definition);
            }

            _settings.RefreshMode = (int)JellyfinRefreshMode.Interval;
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(5));
            State.TickGeneration.Should().Be(5);

            _subject.OnImport(_definition);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(6));
        }

        [Test]
        public void should_keep_failed_interval_imports_pending_when_the_mode_changes()
        {
            Mocker.GetMock<IJellyfinProxy>()
                .SetupSequence(proxy => proxy.RefreshLibrary(_settings))
                .Throws(new Exception("offline"))
                .Pass()
                .Pass();
            _subject.OnImport(_definition);
            _subject.OnImport(_definition);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            _subject.ProcessPending();

            State.ImportedGeneration.Should().Be(2);
            State.ImmediateGeneration.Should().Be(2);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(3));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_isolate_two_connections_to_different_libraries_on_the_same_server()
        {
            var secondSettings = new JellyfinSettings
            {
                Host = _settings.Host,
                ApiKey = _settings.ApiKey,
                LibraryId = "af7440e699464d958939fd71fe016d91",
                RefreshMode = (int)JellyfinRefreshMode.Interval,
                RefreshIntervalMinutes = 60
            };
            var second = Definition(11, secondSettings);
            _definitions.Add(second);
            _subject.ProcessPending();

            _subject.OnImport(_definition);
            _subject.OnImport(second);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Once());
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(secondSettings), Times.Once());
            _repository.All().Should().HaveCount(2);
        }

        [Test]
        public void should_continue_processing_other_connections_when_one_server_fails()
        {
            var secondSettings = new JellyfinSettings
            {
                Host = "second-jellyfin.example",
                ApiKey = "second-key",
                LibraryId = _settings.LibraryId,
                RefreshMode = (int)JellyfinRefreshMode.Interval,
                RefreshIntervalMinutes = 60
            };
            var second = Definition(11, secondSettings);
            _definitions.Add(second);
            _subject.ProcessPending();
            Mocker.GetMock<IJellyfinProxy>().Setup(proxy => proxy.RefreshLibrary(_settings)).Throws(new Exception("offline"));

            _subject.OnImport(_definition);
            _subject.OnImport(second);
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.TickGeneration.Should().Be(0);
            _repository.ForNotification(second.Id).TickGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(secondSettings), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_pending_imports_when_the_api_key_is_rotated()
        {
            _subject.OnImport(_definition);
            var fingerprint = State.TargetFingerprint;
            var stateToken = State.StateToken;
            _settings.ApiKey = "rotated-test-key";
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.TargetFingerprint.Should().Be(fingerprint);
            State.StateToken.Should().Be(stateToken);
            State.TickGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.Is<JellyfinSettings>(settings => settings.ApiKey == "rotated-test-key")), Times.Once());
        }

        [Test]
        public void should_discard_old_pending_imports_when_the_selected_library_changes()
        {
            _subject.OnImport(_definition);
            var previousToken = State.StateToken;
            _settings.LibraryId = "af7440e699464d958939fd71fe016d91";
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            AdvanceMinutes(60);
            _subject.ProcessPending();

            State.StateToken.Should().NotBe(previousToken);
            State.ImportedGeneration.Should().Be(0);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_not_acknowledge_new_imports_after_a_target_is_changed_and_changed_back_during_http()
        {
            _settings.RefreshMode = (int)JellyfinRefreshMode.EachImport;
            var reconfiguredDuringRequest = false;
            Mocker.GetMock<IJellyfinProxy>()
                .Setup(proxy => proxy.RefreshLibrary(_settings))
                .Callback(() =>
                {
                    if (!reconfiguredDuringRequest)
                    {
                        reconfiguredDuringRequest = true;
                        var original = _settings.LibraryId;
                        _settings.LibraryId = "af7440e699464d958939fd71fe016d91";
                        _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
                        _settings.LibraryId = original;
                        _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
                        _subject.OnImport(_definition);
                    }
                });

            _subject.OnImport(_definition);

            State.ImportedGeneration.Should().Be(1);
            State.ImmediateGeneration.Should().Be(0);
            AdvanceMinutes(1);
            _subject.ProcessPending();

            State.ImmediateGeneration.Should().Be(1);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(_settings), Times.Exactly(2));
        }

        [Test]
        public void should_treat_equivalent_guid_forms_as_the_same_library()
        {
            _subject.OnImport(_definition);
            var token = State.StateToken;
            _settings.LibraryId = Guid.Parse(_settings.LibraryId).ToString("D");
            _subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));

            State.StateToken.Should().Be(token);
            State.ImportedGeneration.Should().Be(1);
        }

        [Test]
        public void should_clean_pending_state_when_the_import_trigger_is_disabled()
        {
            _subject.OnImport(_definition);
            _definition.OnDownload = false;
            _definition.OnGrab = true;
            AdvanceMinutes(60);

            _subject.ProcessPending();

            _repository.All().Should().BeEmpty();
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_clean_pending_state_when_the_connection_is_deleted()
        {
            _subject.OnImport(_definition);
            _definitions.Clear();

            _subject.ProcessPending();

            _repository.All().Should().BeEmpty();
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_ignore_an_import_dispatched_for_a_now_disabled_connection()
        {
            _definition.OnDownload = false;

            _subject.OnImport(_definition);

            _repository.All().Should().BeEmpty();
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_ignore_import_events_for_unsaved_connections()
        {
            var unsaved = Definition(0, _settings);

            _subject.OnImport(unsaved);

            State.ImportedGeneration.Should().Be(0);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }

        [Test]
        public void should_handle_very_large_intervals_without_overflowing_datetime()
        {
            _settings.RefreshIntervalMinutes = int.MaxValue;
            _subject.OnImport(_definition);
            AdvanceMinutes(60);

            Action action = () => _subject.ProcessPending();

            action.Should().NotThrow();
            State.TickGeneration.Should().Be(0);
            Mocker.GetMock<IJellyfinProxy>().Verify(proxy => proxy.RefreshLibrary(It.IsAny<JellyfinSettings>()), Times.Never());
        }
    }
}
