using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.TvMaze;

namespace NzbDrone.Core.Test.MetadataSource.TvMaze
{
    [TestFixture]
    public class TvMazeRateLimiterFixture
    {
        [Test]
        public void should_reserve_only_one_immediate_request_across_concurrent_callers()
        {
            var limiter = new TvMazeRateLimiter();
            var results = new bool[8];

            Parallel.For(0, results.Length, index => results[index] = limiter.TryWaitForSlot(TimeSpan.Zero));

            results.Count(result => result).Should().Be(1);
        }

        [Test]
        public void should_reject_waits_shorter_than_the_shared_cooldown_without_sleeping()
        {
            var limiter = new TvMazeRateLimiter();
            limiter.BackOff(TimeSpan.FromMinutes(1));

            limiter.TryWaitForSlot(TimeSpan.FromSeconds(5)).Should().BeFalse();
        }

        [Test]
        public void should_bound_untrusted_retry_after_values_without_overflowing()
        {
            var limiter = new TvMazeRateLimiter();

            limiter.BackOff(TimeSpan.MaxValue);

            limiter.TryWaitForSlot(TimeSpan.FromSeconds(10)).Should().BeFalse();
        }

        [Test]
        public void should_reject_a_negative_wait_budget()
        {
            var limiter = new TvMazeRateLimiter();
            Action act = () => limiter.TryWaitForSlot(TimeSpan.FromSeconds(-1));

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
