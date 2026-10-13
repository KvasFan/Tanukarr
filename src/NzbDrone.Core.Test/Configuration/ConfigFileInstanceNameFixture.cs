using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Options;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigFileInstanceNameFixture : CoreTest<ConfigFileProvider>
    {
        private AppOptions _appOptions;

        [SetUp]
        public void Setup()
        {
            _appOptions = new AppOptions();
            Mocker.SetConstant<IOptions<AppOptions>>(Options.Create(_appOptions));
            Mocker.GetMock<IAppFolderInfo>().SetupGet(v => v.AppDataFolder).Returns(TempFolder);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.ReadAllText(It.IsAny<string>())).Returns("<Config />");
        }

        [Test]
        public void should_default_to_the_product_name()
        {
            Subject.InstanceName.Should().Be(BuildInfo.ProductName);
        }

        [TestCase("SonarrWeeb")]
        [TestCase("Sonarr Pro")]
        [TestCase("")]
        [TestCase("  ")]
        public void should_normalize_former_or_empty_xml_defaults_without_rewriting_config(string instanceName)
        {
            var config = new XElement("Config", new XElement("InstanceName", instanceName)).ToString();
            Mocker.GetMock<IDiskProvider>().Setup(v => v.ReadAllText(It.IsAny<string>())).Returns(config);

            Subject.InstanceName.Should().Be(BuildInfo.ProductName);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("My Anime Library")]
        [TestCase("SonarrWeeb - Office")]
        [TestCase("Office Sonarr")]
        public void should_preserve_custom_xml_instance_names(string instanceName)
        {
            var config = new XElement("Config", new XElement("InstanceName", instanceName)).ToString();
            Mocker.GetMock<IDiskProvider>().Setup(v => v.ReadAllText(It.IsAny<string>())).Returns(config);

            Subject.InstanceName.Should().Be(instanceName);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("SonarrWeeb")]
        [TestCase("Sonarr Pro")]
        public void should_normalize_former_defaults_from_app_options(string instanceName)
        {
            _appOptions.InstanceName = instanceName;

            Subject.InstanceName.Should().Be(BuildInfo.ProductName);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.ReadAllText(It.IsAny<string>()), Times.Never());
        }

        [TestCase("My Anime Library")]
        [TestCase("SonarrWeeb - Office")]
        [TestCase("Office Sonarr")]
        public void should_preserve_custom_instance_names_from_app_options(string instanceName)
        {
            _appOptions.InstanceName = instanceName;

            Subject.InstanceName.Should().Be(instanceName);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.ReadAllText(It.IsAny<string>()), Times.Never());
        }
    }
}
