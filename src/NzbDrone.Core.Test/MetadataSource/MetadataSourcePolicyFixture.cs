using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataSourcePolicyFixture : CoreTest<MetadataSearchService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupProperty(config => config.MetadataSourceOrder, string.Empty);
            Mocker.GetMock<IConfigService>().SetupProperty(config => config.DisabledMetadataSources, string.Empty);
        }

        [Test]
        public void should_keep_defaults_and_append_new_sources_for_older_clients()
        {
            MetadataSourcePolicy.Order(Mocker.Resolve<IConfigService>()).Should().Equal(Enum.GetValues<MetadataSourceType>());
            MetadataSourcePolicy.Save(Mocker.Resolve<IConfigService>(), new List<MetadataSourceType> { MetadataSourceType.AniList }, null);
            MetadataSourcePolicy.Order(Mocker.Resolve<IConfigService>()).Should().StartWith(MetadataSourceType.AniList).And.OnlyHaveUniqueItems();
            MetadataSourcePolicy.Order(Mocker.Resolve<IConfigService>()).Should().BeEquivalentTo(Enum.GetValues<MetadataSourceType>());
        }

        [Test]
        public void should_preserve_omitted_settings()
        {
            MetadataSourcePolicy.Save(Mocker.Resolve<IConfigService>(), new List<MetadataSourceType> { MetadataSourceType.MyAnimeList }, new List<MetadataSourceType> { MetadataSourceType.Tmdb });
            var order = Mocker.Resolve<IConfigService>().MetadataSourceOrder;
            MetadataSourcePolicy.Save(Mocker.Resolve<IConfigService>(), null, null);
            Mocker.Resolve<IConfigService>().MetadataSourceOrder.Should().Be(order);
            MetadataSourcePolicy.Disabled(Mocker.Resolve<IConfigService>()).Should().Equal(MetadataSourceType.Tmdb);
        }

        [Test]
        public void should_reject_invalid_batch_without_mutating_either_setting()
        {
            var config = Mocker.Resolve<IConfigService>();
            Action save = () => MetadataSourcePolicy.Save(config, new List<MetadataSourceType> { MetadataSourceType.AniList }, new List<MetadataSourceType> { (MetadataSourceType)999 });
            save.Should().Throw<ValidationException>();
            config.MetadataSourceOrder.Should().BeEmpty();
            config.DisabledMetadataSources.Should().BeEmpty();
        }

        [Test]
        public void should_reject_duplicates_and_disabling_every_source()
        {
            Action duplicate = () => MetadataSourcePolicy.Save(Mocker.Resolve<IConfigService>(), new List<MetadataSourceType> { MetadataSourceType.Tmdb, MetadataSourceType.Tmdb }, null);
            duplicate.Should().Throw<ValidationException>();
            Action all = () => MetadataSourcePolicy.Save(Mocker.Resolve<IConfigService>(), null, new List<MetadataSourceType>(Enum.GetValues<MetadataSourceType>()));
            all.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_search_enabled_sources_in_saved_order_without_merging_native_identities()
        {
            var config = Mocker.Resolve<IConfigService>();
            MetadataSourcePolicy.Save(config, new List<MetadataSourceType> { MetadataSourceType.AniList, MetadataSourceType.Tvdb }, new List<MetadataSourceType>(Enum.GetValues<MetadataSourceType>()).FindAll(source => source != MetadataSourceType.AniList && source != MetadataSourceType.Tvdb));
            var calls = new List<string>();
            var native = new Mock<IMetadataProvider>();
            native.Setup(provider => provider.SearchForNewSeries("Title")).Callback(() => calls.Add("AniList"))
                .Returns(new List<Series> { new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "10", TvdbId = 0 } });
            Mocker.GetMock<IMetadataProviderFactory>().Setup(factory => factory.GetProvider(MetadataSourceType.AniList)).Returns(native.Object);
            Mocker.GetMock<ISearchForNewSeries>().Setup(provider => provider.SearchForNewSeries("Title", Language.Italian))
                .Callback(() => calls.Add("TVDB")).Returns(new List<Series> { new Series { MetadataSource = MetadataSourceType.Tvdb, TvdbId = 10 } });

            var results = Subject.Search("Title", Language.Italian, null);
            calls.Should().Equal("AniList", "TVDB");
            results.Should().HaveCount(2);
            results[0].ForeignId.Should().Be("10");
            results[0].TvdbId.Should().Be(0);
            results[1].TvdbId.Should().Be(10);
            Mocker.GetMock<IMetadataProviderFactory>().Verify(factory => factory.GetProvider(MetadataSourceType.Tmdb), Times.Never());
        }
    }
}
