using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Test.Common;
using V3Controller = Sonarr.Api.V3.Config.MetadataSourceConfigController;
using V3Mapper = Sonarr.Api.V3.Config.MetadataSourceConfigResourceMapper;
using V3Resource = Sonarr.Api.V3.Config.MetadataSourceConfigResource;
using V5Controller = Sonarr.Api.V5.Settings.MetadataSourceSettingsController;
using V5Mapper = Sonarr.Api.V5.Settings.MetadataSourceSettingsResourceMapper;
using V5Resource = Sonarr.Api.V5.Settings.MetadataSourceSettingsResource;

namespace NzbDrone.Api.Test
{
    [TestFixture]
    public class MetadataSourceSettingsFixture : TestBase
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupProperty(config => config.MetadataSourceOrder, string.Empty);
            Mocker.GetMock<IConfigService>().SetupProperty(config => config.DisabledMetadataSources, string.Empty);
            Mocker.GetMock<IConfigService>().SetupProperty(config => config.TmdbApiKey, "private-test-key");
            Mocker.Resolve<V5Controller>().Url = new Mock<IUrlHelper>().Object;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_mask_credentials_on_both_versions(bool v3)
        {
            var resource = v3 ? (object)V3Mapper.ToResource(Mocker.Resolve<IConfigService>()) : V5Mapper.ToResource(Mocker.Resolve<IConfigService>());
            Json.ToJson(resource).Should().NotContain("private-test-key").And.Contain("********");
            STJson.ToJson(resource).Should().NotContain("private-test-key").And.Contain("********");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_round_trip_order_disable_and_preserve_masked_key(bool v3)
        {
            var order = new List<MetadataSourceType> { MetadataSourceType.MyAnimeList, MetadataSourceType.AniList };
            var disabled = new List<MetadataSourceType> { MetadataSourceType.Tmdb };
            if (v3)
            {
                Mocker.Resolve<V3Controller>().SaveConfig(new V3Resource { Id = 1, TmdbApiKey = "********", MetadataSourceOrder = order, DisabledMetadataSources = disabled });
            }
            else
            {
                Mocker.Resolve<V5Controller>().SaveSettings(new V5Resource { Id = 1, TmdbApiKey = "********", MetadataSourceOrder = order, DisabledMetadataSources = disabled });
            }

            MetadataSourcePolicy.Order(Mocker.Resolve<IConfigService>()).Should().StartWith(order);
            MetadataSourcePolicy.Disabled(Mocker.Resolve<IConfigService>()).Should().Equal(disabled);
            Mocker.Resolve<IConfigService>().TmdbApiKey.Should().Be("private-test-key");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_omitted_key_and_policy_for_older_clients(bool v3)
        {
            Mocker.Resolve<IConfigService>().MetadataSourceOrder = "2,0,1,3";
            Mocker.Resolve<IConfigService>().DisabledMetadataSources = "1";
            if (v3)
            {
                Mocker.Resolve<V3Controller>().SaveConfig(new V3Resource { Id = 1 });
            }
            else
            {
                Mocker.Resolve<V5Controller>().SaveSettings(new V5Resource { Id = 1 });
            }

            Mocker.Resolve<IConfigService>().MetadataSourceOrder.Should().Be("2,0,1,3");
            Mocker.Resolve<IConfigService>().DisabledMetadataSources.Should().Be("1");
            Mocker.Resolve<IConfigService>().TmdbApiKey.Should().Be("private-test-key");
        }
    }
}
