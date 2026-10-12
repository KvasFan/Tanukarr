using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.DailySeries;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DataAugmentation.DailySeries
{
    [TestFixture]
    public class DailySeriesDataProxyFixture : CoreTest<DailySeriesDataProxy>
    {
        [SetUp]
        public void Setup()
        {
            UseRealHttp();
        }

        [Test]
        public void should_return_empty_when_shared_cloud_is_disabled()
        {
            var list = Subject.GetDailySeriesIds();
            list.Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();
        }
    }
}
