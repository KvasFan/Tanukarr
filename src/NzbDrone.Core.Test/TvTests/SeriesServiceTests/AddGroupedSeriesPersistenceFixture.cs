using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.TvTests.SeriesServiceTests
{
    [TestFixture]
    public class AddGroupedSeriesPersistenceFixture : DbTest<SeriesService, Series>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.All()).Returns(() => Storage.All());
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.Get(It.IsAny<int>()))
                .Returns((int id) => Storage.Get(id));
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.Insert(It.IsAny<Series>()))
                .Returns((Series series) => Storage.Insert(series));
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.InsertMany(It.IsAny<IList<Series>>()))
                .Callback((IList<Series> series) => Storage.InsertMany(series));
        }

        [TestCase(AnimeGroupMemberKind.Season)]
        [TestCase(AnimeGroupMemberKind.Movie)]
        public void should_reproduce_the_native_unique_constraint_and_atomic_rollback(AnimeGroupMemberKind kind)
        {
            var first = Member(100, 1, kind);
            var duplicate = Member(101, 1, kind);

            Action insert = () => Storage.InsertMany(new[] { first, duplicate });

            VerifyUniqueConstraint(insert,
                kind == AnimeGroupMemberKind.Season ? nameof(Series.AnimeGroupSeasonNumber) : nameof(Series.AnimeGroupMovieNumber),
                kind == AnimeGroupMemberKind.Season ? "IX_Series_AnimeGroupSeasonSlot" : "IX_Series_AnimeGroupMovieSlot");
            AllStoredModels.Should().BeEmpty();
            first.Id.Should().BeGreaterThan(0, "the inherited repository assigns object IDs before the transaction commits");
        }

        [TestCase(AnimeGroupMemberKind.Season)]
        [TestCase(AnimeGroupMemberKind.Movie)]
        public void should_keep_nonconflicting_batch_members_and_publish_only_committed_ids(AnimeGroupMemberKind kind)
        {
            var first = Member(100, 1, kind);
            var duplicate = Member(101, 1, kind);
            var last = Member(102, 2, kind);

            var added = Subject.AddSeries(new List<Series> { first, duplicate, last }, true);

            added.Should().Equal(first, last);
            AllStoredModels.Select(series => series.TvdbId).Should().BeEquivalentTo(new[] { 100, 102 });
            duplicate.Id.Should().Be(0);
            VerifyImported(added);
        }

        [Test]
        public void should_reject_an_occupied_batch_slot_before_an_atomic_manual_add()
        {
            var first = Member(100, 1);
            var duplicate = Member(101, 1);

            Action add = () => Subject.AddSeries(new List<Series> { first, duplicate });

            add.Should().Throw<ValidationException>().Which.Errors.Should()
                .Contain(error => error.PropertyName == nameof(Series.AnimeGroupSeasonNumber));
            AllStoredModels.Should().BeEmpty();
            VerifyNoImported();
        }

        [TestCase(AnimeGroupMemberKind.Season)]
        [TestCase(AnimeGroupMemberKind.Movie)]
        public void should_recheck_slots_occupied_after_metadata_preparation(AnimeGroupMemberKind kind)
        {
            var candidate = Member(100, 1, kind);
            var valid = Member(101, 2, kind);
            Storage.Insert(Member(200, 1, kind));

            var added = Subject.AddSeries(new List<Series> { candidate, valid }, true);

            added.Should().Equal(valid);
            AllStoredModels.Select(series => series.TvdbId).Should().BeEquivalentTo(new[] { 200, 101 });
            candidate.Id.Should().Be(0);
            VerifyImported(added);
        }

        [TestCase(AnimeGroupMemberKind.Season)]
        [TestCase(AnimeGroupMemberKind.Movie)]
        public void should_recover_a_database_race_without_losing_prepared_members_or_reusing_rolled_back_ids(AnimeGroupMemberKind kind)
        {
            var first = Member(100, 2, kind);
            var occupied = Member(101, 1, kind);
            var last = Member(102, 3, kind);
            var competing = Member(200, 1, kind);
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.InsertMany(It.IsAny<IList<Series>>()))
                .Callback((IList<Series> series) =>
                {
                    Storage.Insert(competing);
                    Storage.InsertMany(series);
                });

            var added = Subject.AddSeries(new List<Series> { first, occupied, last }, true);

            added.Should().Equal(first, last);
            AllStoredModels.Select(series => series.TvdbId).Should().BeEquivalentTo(new[] { 200, 100, 102 });
            occupied.Id.Should().Be(0);
            foreach (var series in added)
            {
                Storage.Get(series.Id).TvdbId.Should().Be(series.TvdbId);
                series.Id.Should().NotBe(competing.Id);
                series.Seasons.Should().ContainSingle().Which.SeasonNumber.Should().Be(1);
            }

            VerifyImported(added);
        }

        [Test]
        public void should_leave_a_failed_atomic_manual_batch_uncommitted_and_reset_rolled_back_ids()
        {
            var first = Member(100, 2);
            var occupied = Member(101, 1);
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.InsertMany(It.IsAny<IList<Series>>()))
                .Callback((IList<Series> series) =>
                {
                    Storage.Insert(Member(200, 1));
                    Storage.InsertMany(series);
                });

            Action add = () => Subject.AddSeries(new List<Series> { first, occupied });

            add.Should().Throw<ValidationException>();
            AllStoredModels.Should().ContainSingle().Which.TvdbId.Should().Be(200);
            first.Id.Should().Be(0);
            occupied.Id.Should().Be(0);
            VerifyNoImported();
        }

        [TestCase(AnimeGroupMemberKind.Season, nameof(Series.AnimeGroupSeasonNumber))]
        [TestCase(AnimeGroupMemberKind.Movie, nameof(Series.AnimeGroupMovieNumber))]
        public void should_translate_a_single_add_database_race_to_a_member_validation_error(AnimeGroupMemberKind kind, string property)
        {
            var candidate = Member(100, 1, kind);
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.Insert(It.IsAny<Series>()))
                .Returns((Series series) =>
                {
                    Storage.Insert(Member(200, 1, kind));
                    return Storage.Insert(series);
                });

            Action add = () => Subject.AddSeries(candidate);

            add.Should().Throw<ValidationException>().Which.Errors.Should().Contain(error => error.PropertyName == property);
            AllStoredModels.Should().ContainSingle().Which.TvdbId.Should().Be(200);
            candidate.Id.Should().Be(0);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<SeriesAddedEvent>()), Times.Never());
        }

        [Test]
        public void should_keep_season_and_movie_number_one_independent()
        {
            var season = Member(100, 1);
            var movie = Member(101, 1, AnimeGroupMemberKind.Movie);

            var added = Subject.AddSeries(new List<Series> { season, movie }, true);

            added.Should().Equal(season, movie);
            AllStoredModels.Should().HaveCount(2);
            VerifyImported(added);
        }

        [Test]
        public void should_preserve_binary_group_id_comparison_and_null_slot_semantics()
        {
            var first = Member(100, 1);
            var differentCase = Member(101, 1);
            first.AnimeGroupId = "manual:CASE";
            differentCase.AnimeGroupId = "manual:case";
            var ungrouped = Member(102, 1);
            var anotherUngrouped = Member(103, 1);
            foreach (var series in new[] { ungrouped, anotherUngrouped })
            {
                series.AnimeGroupingMode = AnimeGroupingMode.Disabled;
                series.AnimeGroupId = null;
                series.AnimeGroupSeasonNumber = null;
            }

            var added = Subject.AddSeries(new List<Series> { first, differentCase, ungrouped, anotherUngrouped }, true);

            added.Should().HaveCount(4);
            AllStoredModels.Should().HaveCount(4);
            VerifyImported(added);
        }

        [Test]
        public void should_not_hide_an_unrelated_tvdb_unique_constraint()
        {
            var first = Member(100, 1);
            var second = Member(100, 2);
            second.TitleSlug = "a-distinct-slug-with-a-duplicate-tvdb-id";
            second.Path = Path.Combine(TempFolder, "Another member");

            Action add = () => Subject.AddSeries(new List<Series> { first, second }, true);

            VerifyUniqueConstraint(add, nameof(Series.TvdbId));
            AllStoredModels.Should().BeEmpty();
            VerifyNoImported();
        }

        [Test]
        public void should_not_hide_an_unrelated_slug_unique_constraint()
        {
            var first = Member(100, 1);
            var second = Member(101, 2);
            second.TitleSlug = first.TitleSlug;

            Action add = () => Subject.AddSeries(new List<Series> { first, second }, true);

            VerifyUniqueConstraint(add, nameof(Series.TitleSlug));
            AllStoredModels.Should().BeEmpty();
            VerifyNoImported();
        }

        [Test]
        public void should_not_hide_an_unexpected_repository_failure()
        {
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.InsertMany(It.IsAny<IList<Series>>()))
                .Throws(new InvalidOperationException("Unexpected repository failure"));

            Action add = () => Subject.AddSeries(new List<Series> { Member(100, 1) }, true);

            add.Should().Throw<InvalidOperationException>();
            AllStoredModels.Should().BeEmpty();
            VerifyNoImported();
        }

        [Test]
        public void should_publish_committed_recovery_members_when_a_later_unrelated_constraint_still_fails()
        {
            var first = Member(100, 2);
            var occupied = Member(101, 1);
            var duplicateId = Member(200, 3);
            duplicateId.TitleSlug = "a-distinct-slug-with-a-duplicate-tvdb-id";
            duplicateId.Path = Path.Combine(TempFolder, "A distinct third member");
            var competing = Member(200, 1);
            Mocker.GetMock<ISeriesRepository>().Setup(repository => repository.InsertMany(It.IsAny<IList<Series>>()))
                .Callback((IList<Series> series) =>
                {
                    Storage.Insert(competing);
                    Storage.InsertMany(series);
                });

            Action add = () => Subject.AddSeries(new List<Series> { first, occupied, duplicateId }, true);

            VerifyUniqueConstraint(add, nameof(Series.TvdbId));
            AllStoredModels.Select(series => series.TvdbId).Should().BeEquivalentTo(new[] { 200, 100 });
            Storage.Get(first.Id).TvdbId.Should().Be(100);
            occupied.Id.Should().Be(0);
            duplicateId.Id.Should().Be(0);
            VerifyImported(new List<Series> { first });
        }

        [Test]
        public void should_defer_a_conflicting_movie_path_even_when_its_number_is_distinct()
        {
            var first = Member(100, 1, AnimeGroupMemberKind.Movie);
            var second = Member(101, 2, AnimeGroupMemberKind.Movie);
            second.Path = first.Path;

            var added = Subject.AddSeries(new List<Series> { first, second }, true);

            added.Should().Equal(first);
            AllStoredModels.Should().ContainSingle();
            VerifyImported(added);
        }

        [Test]
        public void should_allow_only_one_concurrent_single_add_to_claim_the_same_slot()
        {
            using var start = new ManualResetEventSlim();
            var candidates = new[] { Member(100, 1), Member(101, 1) };
            var service = Subject;
            var tasks = candidates.Select(series => Task.Run(() =>
            {
                start.Wait();
                try
                {
                    service.AddSeries(series);
                    return true;
                }
                catch (ValidationException)
                {
                    return false;
                }
            })).ToArray();

            start.Set();
            Task.WaitAll(tasks);

            tasks.Count(task => task.Result).Should().Be(1);
            AllStoredModels.Should().ContainSingle();
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<SeriesAddedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_publish_an_import_event_when_all_prepared_members_are_occupied()
        {
            Storage.Insert(Member(200, 1));

            var added = Subject.AddSeries(new List<Series> { Member(100, 1) }, true);

            added.Should().BeEmpty();
            AllStoredModels.Should().ContainSingle().Which.TvdbId.Should().Be(200);
            VerifyNoImported();
        }

        private Series Member(int tvdbId, int number, AnimeGroupMemberKind kind = AnimeGroupMemberKind.Season)
        {
            return Builder<Series>.CreateNew()
                .With(series => series.Id = 0)
                .With(series => series.TvdbId = tvdbId)
                .With(series => series.Title = "Persistence fixture " + tvdbId)
                .With(series => series.TitleSlug = "persistence-fixture-" + tvdbId)
                .With(series => series.Path = Path.Combine(TempFolder, "Member " + tvdbId))
                .With(series => series.AnimeGroupingMode = AnimeGroupingMode.Manual)
                .With(series => series.AnimeGroupId = "manual:persistence-fixture")
                .With(series => series.AnimeGroupTitle = "Persistence fixture")
                .With(series => series.AnimeGroupMemberKind = kind)
                .With(series => series.AnimeGroupSeasonNumber = kind == AnimeGroupMemberKind.Season ? number : null)
                .With(series => series.AnimeGroupMovieNumber = kind == AnimeGroupMemberKind.Movie ? number : null)
                .With(series => series.Seasons = new List<Season> { new Season { SeasonNumber = 1 } })
                .Build();
        }

        private void VerifyImported(List<Series> added)
        {
            var ids = added.Select(series => series.Id).ToList();
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(
                It.Is<SeriesImportedEvent>(imported => imported.SeriesIds.SequenceEqual(ids))),
                Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<SeriesAddedEvent>()), Times.Never());
        }

        private void VerifyNoImported()
        {
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<SeriesImportedEvent>()), Times.Never());
        }

        private void VerifyUniqueConstraint(Action operation, string column, string constraintName = null)
        {
            if (Db.DatabaseType == DatabaseType.SQLite)
            {
                var exception = operation.Should().Throw<SQLiteException>().Which;
                exception.ResultCode.Should().Be(SQLiteErrorCode.Constraint);
                exception.Message.Should().Contain("Series." + column);
            }
            else
            {
                var exception = operation.Should().Throw<PostgresException>().Which;
                exception.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
                if (constraintName != null)
                {
                    exception.ConstraintName.Should().Be(constraintName);
                }
            }
        }
    }
}
