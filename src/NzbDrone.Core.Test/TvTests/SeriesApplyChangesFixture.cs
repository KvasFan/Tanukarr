using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesApplyChangesFixture
    {
        [TestCase(123, -7, 123)]
        [TestCase(123, 0, 123)]
        [TestCase(-9, -7, -9)]
        [TestCase(-9, 0, -9)]
        [TestCase(-9, 456, 456)]
        [TestCase(123, 456, 456)]
        public void should_only_replace_the_tvdb_identity_with_a_verified_positive_id(int existing, int incoming, int expected)
        {
            var series = new Series { TvdbId = existing };

            series.ApplyChanges(new Series { TvdbId = incoming });

            series.TvdbId.Should().Be(expected);
        }
    }
}
