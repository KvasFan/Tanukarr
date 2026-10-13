using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Nyaa;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NyaaTests
{
    public class NyaaRequestGeneratorFixture : CoreTest<NyaaRequestGenerator>
    {
        private SeasonSearchCriteria _seasonSearchCriteria;
        private AnimeEpisodeSearchCriteria _animeSearchCriteria;
        private AnimeSeasonSearchCriteria _animeSeasonSearchCriteria;

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new NyaaSettings()
            {
                BaseUrl = "http://127.0.0.1:1234/",
            };

            _seasonSearchCriteria = new SeasonSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                SeasonNumber = 1,
            };

            _animeSearchCriteria = new AnimeEpisodeSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                AbsoluteEpisodeNumber = 9,
                SeasonNumber = 1,
                EpisodeNumber = 9
            };

            _animeSeasonSearchCriteria = new AnimeSeasonSearchCriteria()
            {
                SceneTitles = new List<string>() { "Naruto Shippuuden" },
                SeasonNumber = 3
            };
        }

        [Test]
        public void should_not_search_season()
        {
            var results = Subject.GetSearchRequests(_seasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(0);
        }

        [Test]
        public void should_search_season()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_seasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s01");
        }

        [Test]
        public void should_use_only_absolute_numbering_for_anime_search()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetTier(0).Should().HaveCount(2);
            var pages = results.GetTier(0).Take(2).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+9");
            pages[1].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+09");
        }

        [Test]
        public void should_also_use_standard_numbering_for_anime_search()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetTier(0).Should().HaveCount(3);
            var pages = results.GetTier(0).Take(3).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+9");
            pages[1].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+09");
            pages[2].Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s01e09");
        }

        [Test]
        public void should_search_by_standard_season_number()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.FullUri.Should().Contain("term=Naruto+Shippuuden+s03");
        }

        [Test]
        public void should_search_season_title_alias_without_the_season_number()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Naruto Shippuuden" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            results.GetTier(0).Should().HaveCount(1);
            results.GetTier(0).First().First().Url.FullUri.Should().Contain("term=Naruto+Shippuuden");
            results.GetTier(0).First().First().Url.FullUri.Should().NotContain("s03");
        }

        [Test]
        public void should_search_season_title_alias_when_standard_format_search_is_disabled()
        {
            Subject.Settings.AnimeStandardFormatSearch = false;
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Naruto Shippuuden" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            results.GetTier(0).Should().HaveCount(1);
            results.GetTier(0).First().First().Url.FullUri.Should().Contain("term=Naruto+Shippuuden");
            results.GetTier(0).First().First().Url.FullUri.Should().NotContain("s03");
        }

        [Test]
        public void should_only_search_the_season_title_without_the_season_number()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            _animeSeasonSearchCriteria.SceneTitles = new List<string> { "Naruto", "Naruto Shippuuden" };
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Naruto Shippuuden" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            var pages = results.GetTier(0).Select(t => t.First().Url.FullUri).ToList();

            pages.Should().HaveCount(2);
            pages.Should().Contain(p => p.Contains("term=Naruto+s03"));
            pages.Should().Contain(p => p.Contains("term=Naruto+Shippuuden") && !p.Contains("s03"));
        }

        [TestCase("Panty & Stocking with Garterbelt")]
        [TestCase("C#")]
        [TestCase("Rosario + Vampire")]
        [TestCase("Boku no Hero 100%")]
        [TestCase("進撃の巨人")]
        [TestCase("Title&filter=0#fragment")]
        public void should_encode_search_title_without_changing_existing_parameters(string sceneTitle)
        {
            Subject.Settings.AdditionalParameters = "&cats=1_2&filter=0&u=SubsPlease";
            _animeSearchCriteria.SceneTitles = new List<string> { sceneTitle };

            var requests = Subject.GetSearchRequests(_animeSearchCriteria)
                .GetTier(0).Select(t => t.First()).ToList();

            requests.Should().HaveCount(2);

            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                var query = ReadQueryParameters(request.Url.Query);

                request.Url.Fragment.Should().BeEmpty();
                query.Should().HaveCount(5);
                query["page"].Should().Be("rss");
                query["cats"].Should().Be("1_2");
                query["filter"].Should().Be("0");
                query["u"].Should().Be("SubsPlease");
                query["term"].Should().Be(sceneTitle + (i == 0 ? " 9" : " 09"));
            }
        }

        [Test]
        public void should_preserve_cleaned_special_episode_word_separators()
        {
            var criteria = new SpecialEpisodeSearchCriteria
            {
                EpisodeQueryTitles = new[] { "Naruto+Shippuuden Special+Episode" }
            };

            var request = Subject.GetSearchRequests(criteria).GetTier(0).First().First();

            ReadQueryParameters(request.Url.Query)["term"].Should().Be("Naruto Shippuuden Special Episode");
            request.Url.Fragment.Should().BeEmpty();
        }

        private static Dictionary<string, string> ReadQueryParameters(string query)
        {
            return query.Split('&').Select(parameter => parameter.Split('=', 2))
                .ToDictionary(parameter => Uri.UnescapeDataString(parameter[0]),
                              parameter => Uri.UnescapeDataString(parameter[1].Replace('+', ' ')));
        }
    }
}
