using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListProxyFixture : CoreTest<AniListProxy>
    {
        private DateTime _now;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            Mocker.GetMock<IAniListRequestDelay>().SetupGet(delay => delay.UtcNow).Returns(() => _now);
        }

        private AniListProxy AnotherInstance()
        {
            var cache = new AniListMetadataCache(Mocker.Resolve<ICacheManager>(), Mocker.Resolve<IAniListRequestDelay>());
            return new AniListProxy(Mocker.Resolve<IAniListRequestService>(), cache, TestLogger);
        }

        private static string Media(int id, params int[] episodeNumbers)
        {
            return Json.ToJson(new
            {
                data = new
                {
                    Media = new
                    {
                        id,
                        title = new { english = "Test anime" },
                        status = "FINISHED",
                        episodes = 52,
                        airingSchedule = new
                        {
                            nodes = episodeNumbers.Select(episode => new { episode, airingAt = 1600000000L + episode }).ToList()
                        }
                    }
                }
            });
        }

        private void GivenResponses(params string[] responses)
        {
            var pending = new Queue<string>(responses);
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), pending.Dequeue()));
        }

        private void GivenHttpError(HttpStatusCode status)
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), "not-json", status));
        }

        private static string CompleteMedia(int id, int page, bool hasNextPage, params int[] episodeNumbers)
        {
            var response = JObject.Parse(Media(id, episodeNumbers));
            var media = response["data"]["media"];
            media["type"] = "ANIME";
            media["format"] = "TV";
            media["relations"] = new JObject { ["edges"] = new JArray() };
            media["airingSchedule"]["pageInfo"] = new JObject { ["currentPage"] = page, ["hasNextPage"] = hasNextPage };
            return response.ToString();
        }

        [Test]
        public void should_fetch_and_deserialize_metadata_through_the_shared_request_service()
        {
            GivenResponses(Media(253, 1, 2));

            var result = Subject.GetSeriesInfo(253);

            result.Item1.MetadataSource.Should().Be(MetadataSourceType.AniList);
            result.Item1.ForeignId.Should().Be("253");
            result.Item1.Title.Should().Be("Test anime");
            result.Item2.Should().HaveCount(52);
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                    request.Url.ToString() == AniListQueries.BaseUrl &&
                    request.Method == HttpMethod.Post &&
                    request.Headers.ContentType == "application/json" &&
                    request.SuppressHttpError)),
                    Times.Once());
            Mocker.GetMock<IHttpClient>()
                .Verify(client => client.Post(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_keep_the_catalogue_movie_identity_and_one_episode_without_a_declared_count()
        {
            GivenResponses("{\"data\":{\"Media\":{\"id\":253,\"format\":\"MOVIE\",\"title\":{\"english\":\"Catalogue Movie\"},\"episodes\":null,\"duration\":90}}}");
            var result = Subject.GetSeriesInfo(253);
            result.Item1.ForeignId.Should().Be("253");
            result.Item1.MetadataSource.Should().Be(MetadataSourceType.AniList);
            result.Item1.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Movie);
            result.Item1.SeasonTypes.Should().ContainSingle().Which.EpisodeCount.Should().Be(1);
            var episode = result.Item2.Should().ContainSingle().Which;
            episode.ForeignId.Should().Be("253:1");
            episode.SeasonNumber.Should().Be(1);
            episode.EpisodeNumber.Should().Be(1);
            episode.Runtime.Should().Be(90);
        }

        [Test]
        public void should_route_every_airing_schedule_page_through_the_shared_request_service()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(requests.Add)
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader(),
                    requests.Count == 1 ? Media(253, Enumerable.Range(1, 50).ToArray()) : Media(253, 51, 52)));

            var result = Subject.GetSeriesInfo(253);

            requests.Should().HaveCount(2);
            requests.Select(request => (int)JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["page"])
                .Should().Equal(1, 2);
            result.Item2.Where(episode => episode.AirDateUtc.HasValue).Should().HaveCount(52);
            result.Item2.Last().AbsoluteEpisodeNumber.Should().Be(52);
        }

        [Test]
        public void should_route_title_search_through_the_shared_request_service()
        {
            GivenResponses("{\"data\":{\"Page\":{\"media\":[{\"id\":253,\"title\":{\"english\":\"Test anime\"}}]}}}");

            Subject.SearchForNewSeries("Test anime").Should().ContainSingle().Which.ForeignId.Should().Be("253");

            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                    Encoding.UTF8.GetString(request.ContentData).Contains("Test anime"))),
                    Times.Once());
        }

        [Test]
        public void should_keep_distinct_metadata_in_a_65_entry_import_batch()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => new HttpResponse(
                    request,
                    new HttpHeader(),
                    Media((int)JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["id"])));

            var results = Enumerable.Range(1, 65).Select(id => Subject.GetSeriesInfo(id).Item1).ToList();

            results.Select(series => series.ForeignId).Should().Equal(Enumerable.Range(1, 65).Select(id => id.ToString()));
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(65));
        }

        [Test]
        public void should_preserve_not_found_semantics_without_deserializing_error_content()
        {
            GivenHttpError(HttpStatusCode.NotFound);

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<SeriesNotFoundException>();
            Subject.SearchForNewSeriesByAniListId(253).Should().BeEmpty();
        }

        [Test]
        public void should_propagate_exhausted_rate_limit_retries_from_the_shared_service()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => throw new TooManyRequestsException(request,
                    new HttpResponse(request, new HttpHeader { { "Retry-After", "60" } }, "{}", HttpStatusCode.TooManyRequests)));

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<TooManyRequestsException>().Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
            Mocker.GetMock<IAniListRequestService>()
                .Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_return_no_search_results_when_shared_rate_limit_retries_are_exhausted()
        {
            Mocker.GetMock<IAniListRequestService>()
                .Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) => throw new TooManyRequestsException(request,
                    new HttpResponse(request, new HttpHeader(), "{}", HttpStatusCode.TooManyRequests)));

            Subject.SearchForNewSeries("Test anime").Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_propagate_other_http_errors_without_deserializing_error_content()
        {
            GivenHttpError(HttpStatusCode.ServiceUnavailable);

            Action act = () => Subject.GetSeriesInfo(253);

            act.Should().Throw<HttpException>().Which.Response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        [Test]
        public void should_reuse_complete_metadata_and_root_relations_across_add_grouping_and_refresh()
        {
            GivenResponses(CompleteMedia(253, 1, false, 1, 2));

            var first = Subject.GetSeriesInfo(253);
            first.Item1.Title = "Edited by caller";
            first.Item1.Genres.Add("Edited genre");
            first.Item2.Clear();

            var relations = AnotherInstance().GetAnimeRelations("253");
            relations.ForeignId.Should().Be("253");
            relations.PrequelIds.Should().BeEmpty();
            relations.SequelIds.Should().BeEmpty();

            var refreshed = AnotherInstance().GetSeriesInfo(253);
            refreshed.Item1.Title.Should().Be("Test anime");
            refreshed.Item1.Genres.Should().NotContain("Edited genre");
            refreshed.Item2.Should().HaveCount(52);
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_refresh_metadata_after_five_minutes_without_an_unnecessary_root_relation_query()
        {
            GivenResponses(CompleteMedia(253, 1, false, 1), CompleteMedia(253, 1, false, 1));

            Subject.GetSeriesInfo(253);
            _now += TimeSpan.FromMinutes(5);
            AnotherInstance().GetSeriesInfo(253);
            AnotherInstance().GetAnimeRelations("253").Should().NotBeNull();

            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_reuse_complete_public_relations_for_fifteen_minutes()
        {
            GivenResponses(CompleteMedia(253, 1, false), CompleteMedia(253, 1, false));

            Subject.GetAnimeRelations("253").Should().NotBeNull();
            _now += TimeSpan.FromMinutes(14);
            AnotherInstance().GetAnimeRelations("253").Should().NotBeNull();
            _now += TimeSpan.FromMinutes(1);
            AnotherInstance().GetAnimeRelations("253").Should().NotBeNull();

            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_avoid_an_extra_schedule_request_at_an_authoritative_full_page_boundary()
        {
            GivenResponses(CompleteMedia(253, 1, false, Enumerable.Range(1, 50).ToArray()));

            Subject.GetSeriesInfo(253).Item2.Count(episode => episode.AirDateUtc.HasValue).Should().Be(50);

            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_follow_has_next_page_with_a_lightweight_schedule_query_even_after_a_short_page()
        {
            GivenResponses(CompleteMedia(253, 1, true, 1, 2), CompleteMedia(253, 2, false, 3, 4));

            var episodes = Subject.GetSeriesInfo(253).Item2;

            episodes.Count(episode => episode.AirDateUtc.HasValue).Should().Be(4);
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.Is<HttpRequest>(request =>
                JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["page"].Value<int>() == 2 &&
                Encoding.UTF8.GetString(request.ContentData).Contains("airingSchedule") &&
                !Encoding.UTF8.GetString(request.ContentData).Contains("coverImage"))),
                Times.Once());
        }

        [TestCase(1)]
        [TestCase(2)]
        public void should_reject_partial_graphql_metadata_on_any_page_without_caching_it(int failedPage)
        {
            var invalid = JObject.Parse(CompleteMedia(253, failedPage, false, 1));
            invalid["errors"] = JArray.Parse("[{\"message\":\"private-provider-error\"}]");
            if (failedPage == 1)
            {
                GivenResponses(invalid.ToString(), CompleteMedia(253, 1, false, 1));
            }
            else
            {
                GivenResponses(CompleteMedia(253, 1, true, 1), invalid.ToString(), CompleteMedia(253, 1, false, 1));
            }

            Action fetch = () => Subject.GetSeriesInfo(253);
            var error = fetch.Should().Throw<HttpException>().Which;
            error.Response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            error.ToString().Should().NotContain("private-provider-error");
            AnotherInstance().GetSeriesInfo(253).Item1.ForeignId.Should().Be("253");
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(failedPage + 1));
        }

        [TestCase("wrongId")]
        [TestCase("wrongPage")]
        [TestCase("unknownBoundary")]
        [TestCase("emptyNextPage")]
        public void should_reject_inconsistent_schedule_pages_without_reusing_the_partial_result(string invalidKind)
        {
            var invalid = JObject.Parse(CompleteMedia(253, 1, false, 1));
            var media = invalid["data"]["media"];
            switch (invalidKind)
            {
                case "wrongId": media["id"] = 254; break;
                case "wrongPage": media["airingSchedule"]["pageInfo"]["currentPage"] = 2; break;
                case "unknownBoundary": media["airingSchedule"]["pageInfo"]["hasNextPage"] = null; break;
                case "emptyNextPage":
                    media["airingSchedule"]["pageInfo"]["hasNextPage"] = true;
                    media["airingSchedule"]["nodes"] = new JArray();
                    break;
            }

            GivenResponses(invalid.ToString(), CompleteMedia(253, 1, false, 1));
            Action fetch = () => Subject.GetSeriesInfo(253);
            fetch.Should().Throw<HttpException>();
            AnotherInstance().GetSeriesInfo(253).Item1.ForeignId.Should().Be("253");
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_cache_a_schedule_truncated_at_the_existing_twenty_page_bound()
        {
            var calls = 0;
            Mocker.GetMock<IAniListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    calls++;
                    var page = JObject.Parse(Encoding.UTF8.GetString(request.ContentData))["variables"]["page"].Value<int>();
                    return new HttpResponse(request, new HttpHeader(), CompleteMedia(253, page, true, Enumerable.Range(((page - 1) * 50) + 1, 50).ToArray()));
                });

            Subject.GetSeriesInfo(253).Item1.ForeignId.Should().Be("253");
            AnotherInstance().GetSeriesInfo(253).Item1.ForeignId.Should().Be("253");
            calls.Should().Be(40);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void should_not_consume_provider_quota_for_invalid_anilist_ids(int id)
        {
            Action fetch = () => Subject.GetSeriesInfo(id);
            fetch.Should().Throw<SeriesNotFoundException>();
            Subject.SearchForNewSeriesByAniListId(id).Should().BeEmpty();
            Mocker.GetMock<IAniListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }
    }
}
