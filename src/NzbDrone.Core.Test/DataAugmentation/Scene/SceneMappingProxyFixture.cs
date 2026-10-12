using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DataAugmentation.Scene
{
    [TestFixture]
    public class SceneMappingProxyFixture : CoreTest<SceneMappingProxy>
    {
        [SetUp]
        public void Setup()
        {
            UseRealHttp();
        }

        [Test]
        public void should_block_shared_cloud_mappings()
        {
            var exception = Assert.Throws<HttpException>(() => Subject.Fetch());
            exception.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
