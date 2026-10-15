using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NewznabTests
{
    public class NewznabSettingFixture : CoreTest
    {
        [TestCase("http://nzbs.org")]
        [TestCase("http:///www.nzbplanet.net")]
        public void requires_apikey(string url)
        {
            var setting = new NewznabSettings()
            {
                ApiKey = "",
                BaseUrl = url
            };

            setting.Validate().IsValid.Should().BeFalse();
            setting.Validate().Errors.Should().Contain(c => c.PropertyName == nameof(NewznabSettings.ApiKey));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase(null)]
        public void invalid_url_should_not_apikey(string url)
        {
            var setting = new NewznabSettings
            {
                ApiKey = "",
                BaseUrl = url
            };

            setting.Validate().IsValid.Should().BeFalse();
            setting.Validate().Errors.Should().NotContain(c => c.PropertyName == nameof(NewznabSettings.ApiKey));
            setting.Validate().Errors.Should().Contain(c => c.PropertyName == nameof(NewznabSettings.BaseUrl));
        }

        [TestCase("http://nzbs2.org")]
        public void doesnt_requires_apikey(string url)
        {
            var setting = new NewznabSettings()
            {
                ApiKey = "",
                BaseUrl = url
            };

            setting.Validate().IsValid.Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_default_omitted_movie_categories_without_changing_existing_tv_filters(bool torznab)
        {
            const string json = "{\"baseUrl\":\"https://generated.invalid\",\"categories\":[5030,5040],\"animeCategories\":[5070]}";
            var settings = torznab ? Json.Deserialize<TorznabSettings>(json) : Json.Deserialize<NewznabSettings>(json);
            settings.MovieCategories.Should().Equal(2000);
            settings.Categories.Should().Equal(5030, 5040);
            settings.AnimeCategories.Should().Equal(5070);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_allow_a_movie_only_indexer_and_reject_empty_categories(bool torznab)
        {
            var settings = torznab ? new TorznabSettings() : new NewznabSettings();
            settings.BaseUrl = "https://generated.invalid";
            settings.Categories = new int[0];
            settings.AnimeCategories = new int[0];
            settings.Validate().IsValid.Should().BeTrue();
            settings.MovieCategories = new int[0];
            settings.Validate().IsValid.Should().BeFalse();
            NewznabCategoryFieldOptionsConverter.GetFieldSelectOptions(null).Should().Contain(option => option.Value == 2000);
        }
    }
}
