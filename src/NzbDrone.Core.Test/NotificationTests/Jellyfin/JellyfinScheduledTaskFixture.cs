using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinScheduledTaskFixture : DbTest
    {
        private ScheduledTaskRepository _repository;

        [SetUp]
        public void Setup()
        {
            _repository = Mocker.Resolve<ScheduledTaskRepository>();
            Mocker.SetConstant<IScheduledTaskRepository>(_repository);
        }

        [Test]
        public void should_persist_a_one_minute_refresh_task_on_application_start()
        {
            var manager = Mocker.Resolve<TaskManager>();

            manager.Handle(new ApplicationStartedEvent());

            var stored = _repository.GetDefinition(typeof(ProcessPendingJellyfinRefreshCommand));
            stored.Id.Should().BePositive();
            stored.Interval.Should().Be(1);
            manager.GetAll().Should().Contain(task => task.TypeName == typeof(ProcessPendingJellyfinRefreshCommand).FullName);
            manager.GetNextExecution(typeof(ProcessPendingJellyfinRefreshCommand)).Should().Be(stored.LastExecution.AddMinutes(1));
        }

        [Test]
        public void should_preserve_the_existing_task_and_its_cadence_after_application_restart()
        {
            var lastExecution = DateTime.UtcNow.AddMinutes(-5);
            var existing = _repository.Insert(new ScheduledTask
            {
                TypeName = typeof(ProcessPendingJellyfinRefreshCommand).FullName,
                Interval = 99,
                LastExecution = lastExecution
            });
            var manager = Mocker.Resolve<TaskManager>();

            manager.Handle(new ApplicationStartedEvent());

            var stored = _repository.GetDefinition(typeof(ProcessPendingJellyfinRefreshCommand));
            stored.Id.Should().Be(existing.Id);
            stored.Interval.Should().Be(1);
            stored.LastExecution.Should().Be(lastExecution);
            _repository.All().Count(task => task.TypeName == stored.TypeName).Should().Be(1);
            manager.GetPending().Should().Contain(task => task.TypeName == stored.TypeName);
        }

        [Test]
        public void should_persist_an_independent_one_minute_watched_task_without_changing_refresh_cadence()
        {
            var manager = Mocker.Resolve<TaskManager>();
            manager.Handle(new ApplicationStartedEvent());
            var stored = _repository.GetDefinition(typeof(SyncJellyfinWatchedCommand));
            stored.Id.Should().BePositive();
            stored.Interval.Should().Be(1);
            _repository.GetDefinition(typeof(ProcessPendingJellyfinRefreshCommand)).Interval.Should().Be(1);
        }
    }
}
