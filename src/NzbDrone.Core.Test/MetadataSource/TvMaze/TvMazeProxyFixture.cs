using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.TvMaze;
using NzbDrone.Core.MetadataSource.TvMaze.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.TvMaze
{
    [TestFixture]
    public class TvMazeProxyFixture : CoreTest<TvMazeProxy>
    {
        private const string ShowJson = "{\"id\":7,\"name\":\"Generated show\",\"language\":\"English\",\"status\":\"Ended\",\"summary\":\"<p>Owned &amp; generated</p>\",\"premiered\":\"2020-01-01\",\"averageRuntime\":42,\"externals\":{\"thetvdb\":123,\"imdb\":\"tt1234567\"},\"image\":{\"original\":\"https://static.tvmaze.com/uploads/images/original_untouched/1/1.jpg\"},\"rating\":{\"average\":8.2}}";
        private const string EpisodesJson = "[{\"id\":31,\"name\":\"First title\",\"season\":1,\"number\":1,\"type\":\"regular\",\"airdate\":\"2020-01-01\",\"airstamp\":\"2020-01-01T21:00:00+01:00\"},{\"id\":32,\"name\":\"Next season\",\"season\":2,\"number\":1,\"type\":\"regular\"},{\"id\":33,\"name\":\"Owned special\",\"season\":1,\"number\":null,\"type\":\"significant_special\"}]";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ITvMazeRateLimiter>().Setup(limiter => limiter.TryWaitForSlot(It.IsAny<TimeSpan>())).Returns(true);
            Given<TvMazeShowResource>(ShowJson);
            Given<List<TvMazeEpisodeResource>>(EpisodesJson);
        }

        private void Given<T>(string json, HttpStatusCode status = HttpStatusCode.OK)
            where T : new()
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<T>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<T>(new HttpResponse(request, new HttpHeader(), json, status)));
        }

        [Test]
        public void should_deserialize_native_series_and_episode_identity_with_cover_and_specials()
        {
            var result = Subject.GetSeriesInfo(new Series { ForeignId = "7" });

            result.Item1.MetadataSource.Should().Be(MetadataSourceType.TvMaze);
            result.Item1.ForeignId.Should().Be("7");
            result.Item1.TvMazeId.Should().Be(7);
            result.Item1.TvdbId.Should().Be(123);
            result.Item1.Title.Should().Be("Generated show");
            result.Item1.Overview.Should().Be("Owned & generated");
            result.Item1.Runtime.Should().Be(42);
            result.Item1.Ratings.Value.Should().Be(8.2m);
            result.Item1.Images.Single().RemoteUrl.Should().StartWith("https://static.tvmaze.com/");
            result.Item1.Seasons.Select(season => season.SeasonNumber).Should().Equal(0, 1, 2);
            var first = result.Item2.Single(episode => episode.ForeignId == "31");
            first.Title.Should().Be("First title");
            first.AirDateUtc.Should().Be(new DateTime(2020, 1, 1, 20, 0, 0, DateTimeKind.Utc));
            first.TvdbId.Should().Be(0);
            result.Item2.Single(episode => episode.ForeignId == "33").SeasonNumber.Should().Be(0);
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<List<TvMazeEpisodeResource>>(It.Is<HttpRequest>(request =>
                    request.Url.FullUri == "https://api.tvmaze.com/shows/7/episodes?specials=1" && request.RequestTimeout == TimeSpan.FromSeconds(15))),
                Times.Once());
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(-9)]
        public void should_never_invent_a_tvdb_id_from_a_tvmaze_identifier(int? tvdbId)
        {
            Given<TvMazeShowResource>("{\"id\":7,\"name\":\"Native only\",\"externals\":{\"thetvdb\":" + (tvdbId?.ToString() ?? "null") + "}}");
            Subject.GetSeriesInfo(new Series { ForeignId = "7", TvdbId = -22 }).Item1.TvdbId.Should().Be(0);
        }

        [TestCase("0")]
        [TestCase("-7")]
        [TestCase("../7")]
        [TestCase(null)]
        public void should_reject_invalid_native_identifiers_without_any_http_request(string foreignId)
        {
            Action act = () => Subject.GetSeriesInfo(new Series { ForeignId = foreignId });
            act.Should().Throw<SeriesNotFoundException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<TvMazeShowResource>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_reject_a_mismatched_show_identity_before_fetching_episodes()
        {
            Given<TvMazeShowResource>("{\"id\":8,\"name\":\"Different show\"}");
            Action act = () => Subject.GetSeriesInfo(new Series { ForeignId = "7" });
            act.Should().Throw<SeriesNotFoundException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<List<TvMazeEpisodeResource>>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("null")]
        [TestCase("[null]")]
        [TestCase("[{\"id\":31,\"season\":1,\"number\":null}]")]
        [TestCase("[{\"id\":31,\"season\":1,\"number\":1},{\"id\":31,\"season\":1,\"number\":2}]")]
        [TestCase("[{\"id\":31,\"season\":1,\"number\":1},{\"id\":32,\"season\":1,\"number\":1}]")]
        public void should_fail_before_returning_an_incomplete_or_conflicting_episode_list(string episodes)
        {
            Given<List<TvMazeEpisodeResource>>(episodes);
            Action act = () => Subject.GetSeriesInfo(new Series { ForeignId = "7" });
            act.Should().Throw<NzbDroneException>();
        }

        [Test]
        public void should_retain_an_existing_library_when_the_episode_endpoint_suddenly_becomes_empty()
        {
            Given<List<TvMazeEpisodeResource>>("[]");
            Action act = () => Subject.GetSeriesInfo(new Series { ForeignId = "7", Seasons = new List<Season> { new Season { SeasonNumber = 1 } } });
            act.Should().Throw<NzbDroneException>();
        }

        [Test]
        public void should_bound_and_deduplicate_native_search_results()
        {
            Given<List<TvMazeSearchResource>>("[{\"show\":" + ShowJson + "},{\"show\":" + ShowJson + "},{\"show\":{\"id\":0,\"name\":\"Invalid\"}}]");
            Subject.SearchForNewSeries("Owned & generated").Should().ContainSingle().Which.ForeignId.Should().Be("7");
            Mocker.GetMock<IHttpClient>().Verify(
                client => client.Get<List<TvMazeSearchResource>>(It.Is<HttpRequest>(request => request.Url.FullUri.Contains("q=Owned%20%26%20generated"))),
                Times.Once());
        }

        [Test]
        public void should_accept_the_explicit_tvmaze_prefix_without_using_the_episode_endpoint()
        {
            Subject.SearchForNewSeries("tvmaze:7").Should().ContainSingle().Which.TvMazeId.Should().Be(7);
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<List<TvMazeEpisodeResource>>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_retry_429_once_using_the_shared_bounded_limiter()
        {
            var calls = 0;
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<TvMazeShowResource>(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse<TvMazeShowResource>(new HttpResponse(
                    request,
                    new HttpHeader { ["Retry-After"] = "1" },
                    ++calls == 1 ? "{}" : ShowJson,
                    calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)));

            Subject.SearchForNewSeries("tvmaze:7").Should().ContainSingle();
            calls.Should().Be(2);
            Mocker.GetMock<ITvMazeRateLimiter>().Verify(limiter => limiter.BackOff(TimeSpan.FromSeconds(1)), Times.Once());
        }

        [Test]
        public void should_stop_before_http_when_the_shared_limiter_is_cooling_down()
        {
            Mocker.GetMock<ITvMazeRateLimiter>().Setup(limiter => limiter.TryWaitForSlot(It.IsAny<TimeSpan>())).Returns(false);
            Action act = () => Subject.SearchForNewSeries("tvmaze:7");
            act.Should().Throw<NzbDroneException>();
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get<TvMazeShowResource>(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
