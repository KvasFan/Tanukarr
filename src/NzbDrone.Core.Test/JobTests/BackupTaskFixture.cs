using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.JobTests
{
    [TestFixture]
    public class BackupTaskFixture : DbTest
    {
        private ScheduledTaskRepository _repository;
        private TaskManager _manager;
        private ScheduledTask _original;

        [SetUp]
        public void Setup()
        {
            _repository = Mocker.Resolve<ScheduledTaskRepository>();
            Mocker.SetConstant<IScheduledTaskRepository>(_repository);
            _original = _repository.Insert(new ScheduledTask
            {
                TypeName = typeof(BackupCommand).FullName,
                Interval = 24 * 60,
                LastExecution = DateTime.UtcNow.AddDays(-8),
                LastStartTime = DateTime.UtcNow.AddDays(-8).AddMinutes(-1)
            });
            _manager = Mocker.Resolve<TaskManager>();
            _manager.Handle(new ApplicationStartedEvent());
        }

        [TestCase(CommandTrigger.Manual, BackupType.Manual)]
        [TestCase(CommandTrigger.Unspecified, BackupType.Manual)]
        [TestCase(CommandTrigger.Scheduled, BackupType.Scheduled)]
        public void should_record_successful_backup_without_changing_its_archive_type(CommandTrigger trigger, BackupType backupType)
        {
            var command = new BackupCommand { Trigger = trigger };
            var startedAt = DateTime.UtcNow.AddMinutes(-1);
            var before = DateTime.UtcNow;

            _manager.Handle(new CommandExecutedEvent(new CommandModel
            {
                Body = command,
                Status = CommandStatus.Completed,
                StartedAt = startedAt,
                EndedAt = before
            }));

            var stored = _repository.GetDefinition(typeof(BackupCommand));
            var cached = _manager.GetAll().Single(task => task.Id == stored.Id);
            stored.LastExecution.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
            stored.LastStartTime.Should().Be(startedAt);
            cached.LastExecution.Should().Be(stored.LastExecution);
            cached.LastStartTime.Should().Be(startedAt);
            _manager.GetNextExecution(typeof(BackupCommand)).Should().Be(stored.LastExecution.AddMinutes(stored.Interval));
            _manager.GetPending().Should().NotContain(task => task.Id == stored.Id);
            command.Type.Should().Be(backupType);
        }

        [TestCase(CommandTrigger.Manual, CommandStatus.Failed)]
        [TestCase(CommandTrigger.Scheduled, CommandStatus.Failed)]
        [TestCase(CommandTrigger.Manual, CommandStatus.Aborted)]
        [TestCase(CommandTrigger.Scheduled, CommandStatus.Cancelled)]
        [TestCase(CommandTrigger.Manual, CommandStatus.Orphaned)]
        [TestCase(CommandTrigger.Scheduled, CommandStatus.Queued)]
        [TestCase(CommandTrigger.Manual, CommandStatus.Started)]
        public void should_preserve_last_success_and_due_schedule_after_incomplete_backup(CommandTrigger trigger, CommandStatus status)
        {
            var command = new CommandModel
            {
                Body = new BackupCommand { Trigger = trigger },
                Status = status,
                StartedAt = status == CommandStatus.Queued ? null : DateTime.UtcNow.AddMinutes(-1),
                Exception = status == CommandStatus.Failed ? "Backup folder is not writable" : null
            };

            _manager.Handle(new CommandExecutedEvent(command));

            var stored = _repository.GetDefinition(typeof(BackupCommand));
            var cached = _manager.GetAll().Single(task => task.Id == stored.Id);
            stored.LastExecution.Should().Be(_original.LastExecution);
            stored.LastStartTime.Should().Be(_original.LastStartTime);
            cached.LastExecution.Should().Be(_original.LastExecution);
            cached.LastStartTime.Should().Be(_original.LastStartTime);
            _manager.GetNextExecution(typeof(BackupCommand)).Should().Be(stored.LastExecution.AddMinutes(stored.Interval));
            _manager.GetPending().Should().Contain(task => task.Id == stored.Id);
            command.Status.Should().Be(status);
            command.Exception.Should().Be(status == CommandStatus.Failed ? "Backup folder is not writable" : null);
        }

        [Test]
        public void should_keep_successful_manual_backup_time_after_restart()
        {
            _manager.Handle(new CommandExecutedEvent(new CommandModel
            {
                Body = new BackupCommand { Trigger = CommandTrigger.Manual },
                Status = CommandStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-1)
            }));
            var beforeRestart = _repository.GetDefinition(typeof(BackupCommand));

            _manager.Handle(new ApplicationStartedEvent());

            var stored = _repository.GetDefinition(typeof(BackupCommand));
            var cached = _manager.GetAll().Single(task => task.Id == stored.Id);
            stored.Id.Should().Be(_original.Id);
            stored.LastExecution.Should().Be(beforeRestart.LastExecution);
            stored.LastStartTime.Should().Be(beforeRestart.LastStartTime);
            cached.LastExecution.Should().Be(beforeRestart.LastExecution);
            _repository.All().Count(task => task.TypeName == stored.TypeName).Should().Be(1);
        }
    }
}
