using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.Jikan;
using NzbDrone.Core.MetadataSource.Jikan.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.Jikan
{
    [TestFixture]
    public class JikanMovieFixture : CoreTest<JikanProxy>
    {
        private void GivenAnime(string type, int? count, string duration, string episodes = "[]")
        {
            var json = new JObject
            {
                ["data"] = new JObject
                {
                    ["mal_id"] = 21, ["title"] = "Catalogue Movie", ["type"] = type,
                    ["episodes"] = count, ["duration"] = duration, ["status"] = "Finished Airing"
                }
            };
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get<JikanAnimeResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanAnimeResponse>(new HttpResponse(request, new HttpHeader(), json.ToString())));
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get<JikanEpisodeListResponse>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<JikanEpisodeListResponse>(new HttpResponse(request, new HttpHeader(), "{\"data\":" + episodes + "}")));
        }

        [TestCase(null)]
        [TestCase(0)]
        public void should_keep_one_native_episode_for_an_explicit_movie_with_no_episode_pages(int? count)
        {
            GivenAnime("Movie", count, "1 hr 30 min");
            var result = Subject.GetSeriesInfo(21);
            result.Item1.ForeignId.Should().Be("21");
            result.Item1.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Movie);
            result.Item1.SeasonTypes.Should().ContainSingle().Which.EpisodeCount.Should().Be(1);
            var episode = result.Item2.Should().ContainSingle().Subject;
            episode.ForeignId.Should().Be("21:1");
            episode.SeasonNumber.Should().Be(1);
            episode.EpisodeNumber.Should().Be(1);
            episode.AbsoluteEpisodeNumber.Should().Be(1);
            episode.Runtime.Should().Be(90);
        }

        [TestCase("TV")]
        [TestCase("OVA")]
        [TestCase(null)]
        public void should_not_invent_an_episode_for_other_formats_without_counts_or_pages(string type)
        {
            GivenAnime(type, null, "24 min per ep");
            Subject.GetSeriesInfo(21).Item2.Should().BeEmpty();
        }

        [Test]
        public void should_keep_real_episode_page_identity_and_title_when_available()
        {
            GivenAnime("Movie", null, "1 hr 30 min", "[{\"mal_id\":1,\"title\":\"Official Movie Title\",\"aired\":\"2020-01-02T00:00:00Z\"}]");
            var episode = Subject.GetSeriesInfo(21).Item2.Should().ContainSingle().Subject;
            episode.ForeignId.Should().Be("1");
            episode.Title.Should().Be("Official Movie Title");
            episode.AirDate.Should().Be("2020-01-02");
        }

        [TestCase("24 min per ep", 24)]
        [TestCase("1 hr 30 min", 90)]
        [TestCase("2 hr", 120)]
        [TestCase("1 HR 2 MIN", 62)]
        [TestCase("Unknown", 0)]
        [TestCase(null, 0)]
        [TestCase("2147483647 hr", 0)]
        public void should_parse_catalogue_duration_for_tv_and_movies(string duration, int expected)
        {
            GivenAnime("Movie", 1, duration);
            var result = Subject.GetSeriesInfo(21);
            result.Item1.Runtime.Should().Be(expected);
            result.Item2.Should().ContainSingle().Which.Runtime.Should().Be(expected);
        }
    }
}
