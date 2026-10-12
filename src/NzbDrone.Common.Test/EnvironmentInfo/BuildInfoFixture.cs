using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class BuildInfoFixture
    {
        [Test]
        public void should_return_version()
        {
            BuildInfo.Version.Major.Should().BeOneOf(5, 10);
        }

        [Test]
        public void should_get_branch()
        {
            BuildInfo.Branch.Should().NotBe("unknown");
            BuildInfo.Branch.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void should_expose_fork_product_metadata_separately_from_sonarr_version()
        {
            BuildInfo.AppName.Should().Be("Sonarr");
            BuildInfo.ProductName.Should().Be("SonarrWeeb");
            BuildInfo.ProductVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$");
            BuildInfo.Version.Major.Should().Be(10);
        }
    }
}
