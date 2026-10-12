using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SyntheticSeriesIdServiceFixture : CoreTest<SyntheticSeriesIdService>
    {
        private void GivenExistingTvdbIds(params int[] ids)
        {
            var lookup = new Dictionary<int, int>();

            for (var i = 0; i < ids.Length; i++)
            {
                lookup[i + 1] = ids[i];
            }

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.AllSeriesTvdbIds())
                  .Returns(lookup);
        }

        [Test]
        public void should_start_at_minus_one_for_an_empty_library()
        {
            GivenExistingTvdbIds();

            Subject.AllocateTvdbId().Should().Be(-1);
        }

        [Test]
        public void should_start_at_minus_one_when_every_series_has_a_real_id()
        {
            GivenExistingTvdbIds(73255, 81797, 121361);

            Subject.AllocateTvdbId().Should().Be(-1);
        }

        [Test]
        public void should_continue_below_the_lowest_synthetic_id()
        {
            GivenExistingTvdbIds(73255, -1, -2);

            Subject.AllocateTvdbId().Should().Be(-3);
        }

        [Test]
        public void should_not_reuse_an_id_that_is_still_in_use()
        {
            // Reuse would collide with the unique index on the column, and would attach the
            // new series to whatever the existing one already has.
            GivenExistingTvdbIds(-1, -2, -3);

            Subject.AllocateTvdbId().Should().Be(-4);
        }

        [Test]
        public void should_stay_negative_when_a_real_id_is_zero()
        {
            // Zero means "unknown" elsewhere, so it must never be handed out as an id.
            GivenExistingTvdbIds(0, 73255);

            Subject.AllocateTvdbId().Should().Be(-1);
        }

        [Test]
        public void should_reserve_distinct_ids_before_the_library_is_updated()
        {
            GivenExistingTvdbIds();

            var allocated = new[] { Subject.AllocateTvdbId(), Subject.AllocateTvdbId(), Subject.AllocateTvdbId() };

            allocated.Should().Equal(-1, -2, -3);
        }

        [Test]
        public void should_allocate_unique_ids_for_parallel_additions()
        {
            GivenExistingTvdbIds();
            var subject = Subject;
            var allocated = new int[100];

            Parallel.For(0, allocated.Length, index => allocated[index] = subject.AllocateTvdbId());

            allocated.Should().OnlyHaveUniqueItems();
            allocated.Should().BeEquivalentTo(Enumerable.Range(1, 100).Select(id => -id));
        }

        [Test]
        public void should_follow_a_lower_database_id_without_reusing_its_reservations()
        {
            GivenExistingTvdbIds(-1);
            Subject.AllocateTvdbId().Should().Be(-2);

            GivenExistingTvdbIds(-20);
            Subject.AllocateTvdbId().Should().Be(-21);

            GivenExistingTvdbIds(-3);
            Subject.AllocateTvdbId().Should().Be(-22);
        }

        [Test]
        public void should_keep_reserved_ids_when_the_library_becomes_empty()
        {
            GivenExistingTvdbIds(-1);
            Subject.AllocateTvdbId().Should().Be(-2);

            GivenExistingTvdbIds();

            Subject.AllocateTvdbId().Should().Be(-3);
        }

        [Test]
        public void should_fail_when_the_negative_id_range_is_exhausted()
        {
            GivenExistingTvdbIds(int.MinValue);

            Assert.Throws<OverflowException>(() => Subject.AllocateTvdbId());
        }
    }
}
